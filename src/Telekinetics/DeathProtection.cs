using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ControlLib.Telekinetics;

/// <summary>
///     Prevents all death of the player while active.
///     Can be configured to either expire after a given delay, or once a given condition is met.
/// </summary>
public class DeathProtection : UpdatableAndDeletable
{
    private static readonly ConditionalWeakTable<Player, DeathProtection> _activeInstances = new();

    /// <summary>
    ///     The positive condition for removing the protection instance; Always returns <c>true</c>.
    /// </summary>
    /// <remarks>
    ///     When using this predicate, the protection instance is removed as soon as lifetime is depleted.
    /// </remarks>
    public static readonly Predicate<Player> DefaultCondition = static (_) => true;

    /// <summary>
    ///     The negative condition for removing the protection instance; Always returns <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     When using this predicate, the protection instance remains active unless manually removed.
    /// </remarks>
    public static readonly Predicate<Player> NullCondition = static (_) => false;

    /// <summary>
    ///     The targeted player.
    /// </summary>
    public Player Target { get; }

    /// <summary>
    ///     The last known position considered "safe"; If Slugcat is destroyed, it is instead teleported to this position.
    /// </summary>
    public WorldCoordinate? SafePos { get; private set; }

    /// <summary>
    ///     The power value for visual effects.
    /// </summary>
    public float Power { get; }

    /// <summary>
    ///     If greater than <c>0</c>, a cooldown where the player is assumed to be alive; Prevents repeated attempts to revive the player causing no revival at all.
    /// </summary>
    public int SaveCooldown { get; private set; }

    /// <summary>
    ///     An optional lifespan for the protection. If a condition is also provided, this acts as the "grace" timer before the protection can actually expire.
    /// </summary>
    private int lifespan;

    /// <summary>
    ///     An optional condition to determine when the protection should be removed. If omitted, defaults to a countdown using lifespan.
    /// </summary>
    private readonly Predicate<Player> condition;

    /// <summary>
    ///     If true and Slugcat somehow dies while under this protection, it is instead revived.
    /// </summary>
    private readonly bool forceRevive;

    /// <summary>
    ///     Creates a new death protection instance which lasts until the given condition returns <c>true</c>.
    /// </summary>
    /// <param name="target">The player to protect.</param>
    /// <param name="condition">The condition for removing the protection.</param>
    /// <param name="lifespan">The amount of time to wait before <paramref name="condition"/> can be tested.</param>
    /// <param name="safePos">An optional "safe" position, used when the player's character is destroyed.</param>
    /// <param name="forceRevive">If true and the player somehow dies while protected, they are revived. This also immediately ends the protection.</param>
    private DeathProtection(Player target, Predicate<Player>? condition, int lifespan, WorldCoordinate? safePos, bool forceRevive)
    {
        Target = target;
        SafePos = safePos ?? target.abstractCreature.pos;

        Power = MindBlast.TryGetInstance(target, out MindBlast mindBlast) ? mindBlast.Power : 1f;

        this.condition = condition ?? DefaultCondition;
        this.forceRevive = forceRevive;
        this.lifespan = lifespan;

        ToggleImmunities(target.abstractCreature, true);
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (Target is null)
        {
            Main.Logger?.LogWarning($"Player was destroyed while being protected! Removing protection object.");
            Destroy();
            return;
        }

        Target.rainDeath = 0f;
        Target.airInLungs = 1f;

        if (ModManager.Watcher)
            Target.repelLocusts = 10 * Math.Max(lifespan, 2);

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
                Main.Logger?.LogInfo($"Moving DeathProtection object to player room.");

                room?.RemoveObject(this);
                Target.room.AddObject(this);
            }

            foreach (RoomCamera camera in room!.game.cameras)
            {
                camera.hud?.textPrompt?.gameOverMode = false;
            }

            if (SafePos is null || ShouldUpdateSafePos())
            {
                SafePos = room.GetWorldCoordinate(Target.bodyChunks[1].pos);
            }

            foreach (WormGrass wormGrass in room.updateList.OfType<WormGrass>())
            {
                foreach (WormGrass.WormGrassPatch patch in wormGrass.patches)
                {
                    patch.trackedCreatures.RemoveAll(tc => tc.creature == Target);
                }

                wormGrass.AddNewRepulsiveObject(Target);
            }
        }

        if (SaveCooldown > 0)
            SaveCooldown--;

        if (Target.dead)
        {
            bool canRevive = forceRevive && SaveCooldown == 0;

            Main.Logger?.LogWarning($"{Target} was killed while protected! Will revive? {canRevive}");

            if (canRevive)
                RevivePlayer();

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

    public override void Destroy()
    {
        base.Destroy();

        if (Target is not null)
        {
            _activeInstances.Remove(Target);

            ToggleImmunities(Target.abstractCreature, false);
        }

        Main.Logger?.LogDebug($"{Target} is no longer being protected.");
    }

    private void RevivePlayer()
    {
        Target.dead = false;

        if (Target.State is HealthState healthState)
        {
            healthState.alive = true;
            healthState.health = 1f;
        }
        else
        {
            Target.playerState.alive = true;

            if (ModManager.CoopAvailable)
            {
                Target.playerState.permaDead = false;
            }
        }

        Target.AI?.abstractAI.SetDestinationNoPathing(Target.abstractCreature.pos, false);

        SaveCooldown = 10;

        Main.Logger?.LogDebug($"Revived {Target}!");
    }

    private bool ShouldUpdateSafePos()
    {
        if (SaveCooldown != 0
            || Target is not { dead: false, grabbedBy.Count: 0 }
            || !Target.IsTileSolid(1, 0, -1)
            || Target.IsTileSolid(1, 0, 0)
            || Target.IsTileSolid(1, 0, 1)) return false;

        Room.Tile tile = room.GetTile(Target.bodyChunks[1].pos);

        return !tile.DeepWater && !tile.wormGrass;
    }

    /// <summary>
    ///     Creates a new death protection instance which lasts for the given amount of ticks.
    /// </summary>
    /// <param name="target">The player to protect.</param>
    /// <param name="lifespan">The duration of the protection.</param>
    /// <param name="safePos">An optional "safe" position, used when the player's character is destroyed.</param>
    /// <param name="forceRevive">If true and the player somehow dies while protected, they are revived. This also immediately ends the protection.</param>
    public static void CreateInstance(Player target, int lifespan, WorldCoordinate? safePos = null, bool forceRevive = true)
    {
        if (CreateInstanceInternal(target, null, lifespan, safePos, forceRevive))
            Main.Logger?.LogInfo($"Preventing all death from {target} for {lifespan} ticks.");
    }

    /// <inheritdoc cref="DeathProtection(Player,Predicate{Player},int,WorldCoordinate?,bool)"/>
    public static void CreateInstance(Player target, Predicate<Player> condition, WorldCoordinate? safePos = null, bool forceRevive = true)
    {
        if (CreateInstanceInternal(target, condition, 40, safePos, forceRevive))
            Main.Logger?.LogInfo($"Preventing all death from {target} {(condition == NullCondition ? "indefinitely" : "conditionally")}.");
    }

    public static bool HasProtection(Player? target) => target is not null && _activeInstances.TryGetValue(target, out _);

    public static bool TryGetProtection(Player? target, out DeathProtection protection)
    {
        if (target is null)
        {
            protection = null!;
            return false;
        }

        return _activeInstances.TryGetValue(target, out protection);
    }

    private static bool CreateInstanceInternal(Player target, Predicate<Player>? condition, int lifespan, WorldCoordinate? safePos, bool forceRevive)
    {
        if (target?.room is null || TryGetProtection(target, out _)) return false;

        DeathProtection protection = new(target, condition, lifespan, safePos, forceRevive);

        if (target.dead)
        {
            protection.RevivePlayer();
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