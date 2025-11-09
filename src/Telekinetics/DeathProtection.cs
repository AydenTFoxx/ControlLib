using System;
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
    ///     The targeted player.
    /// </summary>
    public Player Target { get; }

    /// <summary>
    ///     The last known position considered "safe"; If Slugcat is destroyed, it is instead teleported to this position.
    /// </summary>
    public WorldCoordinate? SafePos { get; }

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
    ///     Determines whether or not a custom condition was provided.
    /// </summary>
    private readonly bool isDefaultCondition;

    /// <summary>
    ///     If true and Slugcat somehow dies while under this protection, it is instead revived.
    /// </summary>
    private readonly bool forceRevive;

    /// <summary>
    ///     Creates a new death protection instance which lasts for the given amount of ticks.
    /// </summary>
    /// <param name="target">The player to protect.</param>
    /// <param name="lifespan">The duration of the protection.</param>
    /// <param name="safePos">An optional "safe" position, used when the player's character is destroyed.</param>
    /// <param name="forceRevive">If true and the player somehow dies while protected, it will be revived. This also immediately ends the protection.</param>
    /// <param name="power">The power multiplier for visual effects</param>
    private DeathProtection(Player target, int lifespan, WorldCoordinate? safePos, bool forceRevive, float power)
        : this(target, null, safePos, forceRevive, power)
    {
        this.lifespan = lifespan;
    }

    /// <summary>
    ///     Creates a new death protection instance which lasts until the given condition returns <c>true</c>.
    /// </summary>
    /// <param name="target">The player to protect.</param>
    /// <param name="condition">The condition for removing the protection.</param>
    /// <param name="safePos">An optional "safe" position, used when the player's character is destroyed.</param>
    /// <param name="forceRevive">If true and the player somehow dies while protected, it will be revived. This also immediately ends the protection.</param>
    /// <param name="power">The power multiplier for visual effects</param>
    private DeathProtection(Player target, Predicate<Player>? condition, WorldCoordinate? safePos, bool forceRevive, float power)
    {
        Target = target;
        SafePos = safePos ?? target.abstractCreature.pos;
        Power = power;

        this.condition = condition ?? DefaultCondition;
        this.forceRevive = forceRevive;

        isDefaultCondition = condition is null;
        lifespan = isDefaultCondition ? 1 : 40;
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        Target.rainDeath = 0f;
        Target.airInLungs = 1f;

        if (ModManager.Watcher)
            Target.repelLocusts = 10 * lifespan;

        foreach (Creature.Grasp grasp in Target.grabbedBy)
        {
            Creature grabber = grasp.grabber;

            if (grabber is not Player)
            {
                grabber.ReleaseGrasp(grasp.graspUsed);
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

            Target.room.game.cameras[0].hud?.textPrompt?.gameOverMode = false;
        }

        if (SaveCooldown > 0)
            SaveCooldown--;

        if (Target.dead)
        {
            bool canRevive = forceRevive && SaveCooldown == 0;

            Main.Logger?.LogWarning($"{Target} was killed while protected! Will revive? {canRevive}");

            if (canRevive)
                RevivePlayer(Target);

            Destroy();
            return;
        }

        if (!isDefaultCondition && lifespan > 0)
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

        _activeInstances.Remove(Target);

        Main.Logger?.LogDebug($"{Target} is no longer being protected.");
    }

    private void RevivePlayer(Player target)
    {
        target.dead = false;

        if (target.State is HealthState healthState)
        {
            healthState.alive = true;
            healthState.health = 1f;
        }
        else
        {
            target.playerState.alive = true;

            if (ModManager.CoopAvailable)
            {
                target.playerState.permaDead = false;
            }
        }

        target.AI?.abstractAI.SetDestinationNoPathing(target.abstractCreature.pos, false);

        if (target.slatedForDeletetion && SafePos.HasValue)
        {
            target.slatedForDeletetion = false;
            target.SuperHardSetPosition(target.room.MiddleOfTile(SafePos.Value));
        }

        SaveCooldown = 10;

        Main.Logger?.LogDebug($"Revived {target}!");
    }

    private bool DefaultCondition(Player _)
    {
        lifespan--;

        return lifespan <= 0;
    }

    /// <inheritdoc cref="DeathProtection(Player,int,WorldCoordinate?,bool,float)"/>
    public static void CreateInstance(Player target, int lifespan, WorldCoordinate? safePos = null, bool forceRevive = true, float power = 1f)
    {
        if (target?.room is null || TryGetProtection(target, out _)) return;

        DeathProtection protection = new(target, lifespan, safePos, forceRevive, power);

        _activeInstances.Add(target, protection);

        target.room.AddObject(protection);

        Main.Logger?.LogInfo($"Preventing all death from {target} for {lifespan} ticks.");
    }

    /// <inheritdoc cref="DeathProtection(Player,Predicate{Player},WorldCoordinate?,bool,float)"/>
    public static void CreateInstance(Player target, Predicate<Player> condition, WorldCoordinate? safePos = null, bool forceRevive = true, float power = 1f)
    {
        if (target?.room is null || TryGetProtection(target, out _)) return;

        DeathProtection protection = new(target, condition, safePos, forceRevive, power);

        _activeInstances.Add(target, protection);

        target.room.AddObject(protection);

        Main.Logger?.LogInfo($"Preventing all death from {target} conditionally.");
    }

    public static bool TryGetProtection(Player target, out DeathProtection protection) => _activeInstances.TryGetValue(target, out protection);
}