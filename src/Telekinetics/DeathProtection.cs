using System;
using System.Collections.Generic;
using System.Linq;
using ControlLib.Possession;
using ModLib.Collections;

namespace ControlLib.Telekinetics;

/// <summary>
///     Prevents all death of its target creature while active.
///     Can be configured to either expire after a given delay, or once a given condition is met.
/// </summary>
public class DeathProtection : UpdatableAndDeletable
{
    private static readonly WeakDictionary<Creature, DeathProtection> _activeInstances = [];

    /// <summary>
    ///     The positive condition for removing the protection instance; Always returns <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     When using this predicate, the protection instance is removed as soon as lifetime is depleted.
    /// </remarks>
    public static readonly Predicate<Creature> DefaultCondition = static (_) => true;

    /// <summary>
    ///     The negative condition for removing the protection instance; Always returns <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     When using this predicate, the protection instance remains active unless manually removed.
    /// </remarks>
    public static readonly Predicate<Creature> NullCondition = static (_) => false;

    /// <summary>
    ///     The targeted creature.
    /// </summary>
    public Creature Target { get; }

    /// <summary>
    ///     The last known position considered "safe"; If the protected creature is destroyed, it is instead teleported to this position.
    /// </summary>
    public WorldCoordinate? SafePos { get; private set; }

    /// <summary>
    ///     The power value for visual effects.
    /// </summary>
    public float Power { get; }

    /// <summary>
    ///     If greater than <c>0</c>, a cooldown where the creature is assumed to be alive; Prevents repeated attempts to revive the target causing no revival at all.
    /// </summary>
    public int SaveCooldown { get; private set; }

    /// <summary>
    ///     An optional lifespan for the protection. If a condition is also provided, this acts as the "grace" timer before the protection can actually expire.
    /// </summary>
    private int lifespan;

    /// <summary>
    ///     An optional condition to determine when the protection should be removed. If omitted, defaults to a countdown using lifespan.
    /// </summary>
    private readonly Predicate<Creature> condition;

    /// <summary>
    ///     If true and Target somehow dies while under this protection, it is instead revived.
    /// </summary>
    private readonly bool forceRevive;

    /// <summary>
    ///     If true, deep bodies of water are considered as "safe positions" for returning Target in case of destruction.
    /// </summary>
    private readonly bool isWaterBreathingCrit;

    /// <summary>
    ///     The BodyChunk to use as reference for setting and retrieving the target creature's "safe position".
    /// </summary>
    private readonly int safeChunkIndex;

    /// <summary>
    ///     Creates a new death protection instance which lasts until the given condition returns <c>true</c>.
    /// </summary>
    /// <param name="target">The creature to protect.</param>
    /// <param name="condition">The condition for removing the protection.</param>
    /// <param name="lifespan">The amount of time to wait before <paramref name="condition"/> can be tested.</param>
    /// <param name="safePos">An optional "safe" position, used when the creature is destroyed.</param>
    /// <param name="forceRevive">If true and <paramref name="target"/> somehow dies while protected, it is revived. This also immediately ends the protection.</param>
    private DeathProtection(Creature target, Predicate<Creature>? condition, int lifespan, WorldCoordinate? safePos, bool forceRevive)
    {
        Target = target;
        SafePos = safePos ?? target.abstractCreature.pos;

        Power = MindBlast.TryGetInstance(target as Player, out MindBlast mindBlast) ? mindBlast.Power : 1f;

        this.condition = condition ?? DefaultCondition;
        this.forceRevive = forceRevive;
        this.lifespan = lifespan;

        isWaterBreathingCrit = target.abstractCreature.creatureTemplate.waterRelationship == CreatureTemplate.WaterRelationship.Amphibious
                            || target.abstractCreature.creatureTemplate.waterRelationship == CreatureTemplate.WaterRelationship.WaterOnly;

        safeChunkIndex = Target is Player ? 1 : Target.mainBodyChunkIndex;

        ToggleImmunities(target.abstractCreature, true);
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (Target is null)
        {
            Main.Logger.LogWarning($"Target was destroyed while being protected! Removing protection object.");
            Destroy(false);
            return;
        }

        Target.rainDeath = 0f;

        if (Target is Player player)
        {
            player.airInLungs = 1f;
            player.playerState.permanentDamageTracking = 0d;

            if (ModManager.Watcher)
            {
                player.rippleDeathTime = 0;
                player.rippleDeathIntensity = 0f;
            }
        }
        else if (Target is AirBreatherCreature airBreatherCreature)
            airBreatherCreature.lungs = 1f;

        if (Target.State is HealthState healthState)
        {
            healthState.health = 1f;
            healthState.alive = true;
        }

        if (ModManager.Watcher)
            Target.repelLocusts = Math.Max(Target.repelLocusts, 10 * Math.Max(lifespan, 2));

        for (int i = 0; i < Target.grabbedBy.Count; i++)
        {
            Creature.Grasp grasp = Target.grabbedBy[i];

            if (grasp is null) continue;

            Creature grabber = grasp.grabber;

            if (grabber is not Player)
            {
                grabber.ReleaseGrasp(grasp.graspUsed);
                grabber.Stun(20);
            }
        }

        if (Target.room is not null)
        {
            if (Target.room != room)
            {
                Main.Logger.LogInfo($"Moving DeathProtection object to Target room.");

                room?.RemoveObject(this);
                Target.room.AddObject(this);
            }

            foreach (RoomCamera camera in Target.room.game.cameras)
            {
                camera.hud?.textPrompt?.gameOverMode = false;
            }

            if (SafePos is null || ShouldUpdateSafePos())
            {
                SafePos = Target.room.GetWorldCoordinate(Target.bodyChunks[safeChunkIndex].pos);
            }

            foreach (WormGrass wormGrass in Target.room.updateList.OfType<WormGrass>())
            {
                foreach (WormGrass.WormGrassPatch patch in wormGrass.patches)
                {
                    patch.trackedCreatures.RemoveAll(tc => tc.creature == Target);
                }

                foreach (WormGrass.Worm worm in from WormGrass.Worm worm in wormGrass.worms
                                                where worm.focusCreature == Target
                                                select worm)
                {
                    worm.focusCreature = null;
                }

                wormGrass.AddNewRepulsiveObject(Target);
            }
        }
        else if (this is { Target.inShortcut: false, SafePos: not null })
        {
            Main.Logger.LogWarning($"{Target} not found in a room while being protected! Performing saving throw to prevent destruction.");

            PossessionHooks.TrySaveFromDestruction(Target);
        }

        if (SaveCooldown > 0)
            SaveCooldown--;

        if (Target.dead)
        {
            bool canRevive = this is { forceRevive: true, SaveCooldown: 0 };

            Main.Logger.LogWarning($"{Target} was killed while protected! Will revive? {canRevive}");

            if (canRevive)
                ReviveTarget();

            Destroy();
            return;
        }

        if (lifespan > 0)
        {
            lifespan--;
            return;
        }

        if (condition.Invoke(Target))
            Destroy();
    }

    public override void Destroy() => Destroy(true);

    public void Destroy(bool warnIfNull)
    {
        base.Destroy();

        if (Target is not null)
        {
            ToggleImmunities(Target.abstractCreature, false);

            _activeInstances.Remove(Target);

            Main.Logger.LogDebug($"{Target} is no longer being protected.");
        }
        else
        {
            if (warnIfNull)
                Main.Logger.LogWarning($"Protection object was destroyed while Target was null! Attempting to remove instance directly.");

            foreach (KeyValuePair<Creature, DeathProtection> kvp in _activeInstances)
            {
                if (kvp.Value == this)
                {
                    Main.Logger.LogInfo($"Removing detached protection instance from: {kvp.Key}");

                    _activeInstances.Remove(kvp.Key);
                    return;
                }
            }

            Main.Logger.LogWarning($"Protection instance not found within active instances; Assumed to be inaccessible or managed by someone else.");
        }
    }

    private void ReviveTarget()
    {
        Target.dead = false;

        if (Target.State is HealthState healthState)
        {
            healthState.alive = true;
            healthState.health = 1f;
        }
        else if (Target is Player player)
        {
            player.playerState.alive = true;

            if (ModManager.CoopAvailable)
            {
                player.playerState.permaDead = false;
            }
        }

        Target.abstractCreature.abstractAI?.SetDestinationNoPathing(Target.abstractCreature.pos, false);

        SaveCooldown = 10;

        Main.Logger.LogDebug($"Revived {Target}!");
    }

    private bool ShouldUpdateSafePos()
    {
        if (this is { SaveCooldown: 0, Target.dead: false, Target.grabbedBy.Count: 0 }
            && Target.IsTileSolid(1, 0, -1)
            && !Target.IsTileSolid(1, 0, 0)
            && !Target.IsTileSolid(1, 0, 1))
        {
            Room.Tile tile = Target.room.GetTile(Target.bodyChunks[safeChunkIndex].pos);

            return (!tile.DeepWater || isWaterBreathingCrit) && !tile.wormGrass;
        }

        return false;
    }

    /// <summary>
    ///     Creates a new death protection instance which lasts for the given amount of ticks.
    /// </summary>
    /// <param name="target">The creature to protect.</param>
    /// <param name="lifespan">The duration of the protection.</param>
    /// <param name="safePos">An optional "safe" position, used when the creature is destroyed.</param>
    /// <param name="forceRevive">If true and <paramref name="target"/> somehow dies while protected, it is revived. This also immediately ends the protection.</param>
    public static void CreateInstance(Creature target, int lifespan, WorldCoordinate? safePos = null, bool forceRevive = true)
    {
        if (CreateInstanceInternal(target, null, lifespan, safePos, forceRevive))
            Main.Logger.LogInfo($"Preventing all death from {target} for {lifespan} ticks.");
    }

    /// <inheritdoc cref="DeathProtection(Player,Predicate{Player},int,WorldCoordinate?,bool)"/>
    public static void CreateInstance(Creature target, Predicate<Creature> condition, WorldCoordinate? safePos = null, bool forceRevive = true)
    {
        if (CreateInstanceInternal(target, condition, 40, safePos, forceRevive))
            Main.Logger.LogInfo($"Preventing all death from {target} {(condition == NullCondition ? "indefinitely" : "conditionally")}.");
    }

    public static bool HasProtection(Creature? target) => target is not null && _activeInstances.TryGetValue(target, out _);

    public static bool TryGetProtection(Creature? target, out DeathProtection protection)
    {
        if (target is null)
        {
            protection = null!;
            return false;
        }

        return _activeInstances.TryGetValue(target, out protection);
    }

    private static bool CreateInstanceInternal(Creature target, Predicate<Creature>? condition, int lifespan, WorldCoordinate? safePos, bool forceRevive)
    {
        if (target?.room is null || HasProtection(target)) return false;

        DeathProtection protection = new(target, condition, lifespan, safePos, forceRevive);

        if (target.dead)
        {
            protection.ReviveTarget();
        }

        _activeInstances.Add(target, protection);

        target.room.AddObject(protection);

        return true;
    }

    private static void ToggleImmunities(AbstractCreature? target, bool enable)
    {
        if (target is null) return;

        target.lavaImmune = enable;
        target.tentacleImmune = enable;
        target.HypothermiaImmune = enable;
    }
}