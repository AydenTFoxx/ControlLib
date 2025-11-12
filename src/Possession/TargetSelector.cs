using System;
using System.Collections.Generic;
using System.Linq;
using ControlLib.Possession.Graphics;
using ControlLib.Telekinetics;
using ModLib;
using ModLib.Collections;
using ModLib.Input;
using RWCustom;
using UnityEngine;
using static ModLib.Options.OptionUtils;

namespace ControlLib.Possession;

/// <summary>
/// Selects creatures for possession based on relevance and distance to the player.
/// </summary>
/// <param name="player">The player itself.</param>
/// <param name="manager">The player's <c>PossessionManager</c> instance.</param>
public partial class TargetSelector(Player player, PossessionManager manager) : IDisposable
{
    public WeakList<Creature> Targets { get; protected set; } = [];
    public TargetSelectionState State { get; protected set; } = TargetSelectionState.Idle;
    public TargetInput Input { get; protected set; } = new();

    public virtual bool HasValidTargets
    {
        get
        {
            if (Targets is null or { Count: 0 }) return false;

            foreach (Creature target in Targets)
            {
                if (!IsValidSelectionTarget(target) || target.room != player.room)
                {
                    Targets.Remove(target);
                }
            }

            return Targets.Count > 0;
        }
    }

    public bool HasTargetCursor => targetCursor is not null;

    public bool HasMindBlast => IsOptionEnabled(Options.MIND_BLAST) && (MindBlast.HasInstance(player) || manager.OnMindBlastCooldown);

    public bool ExceededTimeLimit
    {
        get
        {
            if (Input.InputTime > Manager.PossessionTimePotential)
            {
                field = true;
            }
            return field;
        }
        set => field = value;
    }

    public Player Player => player;
    public PossessionManager Manager => manager;

    protected WeakList<Creature> queryCreatures = [];
    protected TargetCursor? targetCursor =
        IsClientOptionValue(Options.SELECTION_MODE, "ascension")
            ? new(manager)
            : null;

    private bool disposedValue;

    /// <summary>
    /// Applies the current selection of targets for possession.
    /// </summary>
    public virtual void ApplySelectedTargets()
    {
        MoveToState(TargetSelectionState.Ready);

        State.UpdatePhase(this);
    }

    public Vector2 GetTargetPos() =>
        targetCursor?.GetPos() ?? (HasValidTargets ? Targets[0].mainBodyChunk.pos : Vector2.zero);

    public void QueryTargetCursor()
    {
        if (State is not QueryingState queryingState) return;

        queryCreatures = QueryCreatures(player, targetCursor);
        queryingState.UpdatePhase(this, isRecursive: true);

        Input.QueriedCursor = true;

        targetCursor?.ResetCursor();
    }

    /// <summary>
    /// Moves the internal state machine to the given state.
    /// </summary>
    /// <param name="state">The new state of the state machine.</param>
    public void MoveToState(TargetSelectionState state)
    {
        try
        {
            State = State.MoveToState(state);
        }
        catch (Exception ex)
        {
            Main.Logger?.LogError($"Failed to move to state {state}!");
            Main.Logger?.LogError(ex);
        }
    }

    /// <summary>
    /// Resets the selector's input data. Also restores the player's controls if they have no possession.
    /// </summary>
    public void ResetSelectorInput(bool forceReset = false)
    {
        Input = new();

        if (!Manager.IsPossessing)
        {
            PossessionManager.DestroyFadeOutController(player);
        }

        ExceededTimeLimit = false;

        if (HasMindBlast || forceReset)
        {
            targetCursor?.ResetCursor();

            MoveToState(TargetSelectionState.Idle);
        }
    }

    public void ResetTargetCursor()
    {
        targetCursor?.Destroy();

        if (IsClientOptionValue(Options.SELECTION_MODE, "ascension"))
            targetCursor = new TargetCursor(Manager);
    }

    /// <summary>
    /// Updates the target selector's behaviors.
    /// </summary>
    public virtual void Update()
    {
        if (Input.LockAction) return;

        Input.InputTime++;

        if (ExceededTimeLimit || Manager.PossessionCooldown > 0)
        {
            if (ExceededTimeLimit && !HasMindBlast)
            {
                Manager.PossessionCooldown = 80;

                player.exhausted = true;
                player.aerobicLevel = 1f;
            }

            Input.LockAction = true;
            return;
        }

        State.UpdatePhase(this);

        bool hasValidTargets = HasValidTargets;

        if (player.graphicsModule is PlayerGraphics playerGraphics
            && (hasValidTargets || targetCursor is not null))
        {
            if (targetCursor is not null)
            {
                playerGraphics.LookAtPoint(targetCursor.GetPos(), 9000f);
            }
            else if (hasValidTargets)
            {
                playerGraphics.LookAtObject(Targets[0]);
            }
            else
            {
                playerGraphics.LookAtNothing();
            }

            int handIndex = playerGraphics.hands[0].mode == Limb.Mode.Retracted ? 0 : 1;

            Vector2 targetPos = targetCursor?.GetPos() ?? (hasValidTargets ? Targets[0].mainBodyChunk.pos : Vector2.zero);
            targetPos = RWCustomExts.ClampedDist(targetPos, player.mainBodyChunk.pos, 80f);

            playerGraphics.hands[handIndex].mode = Limb.Mode.HuntAbsolutePosition;
            playerGraphics.hands[handIndex].absoluteHuntPos = targetPos;
        }

        if (hasValidTargets)
        {
            int module = Extras.IsMultiplayer && !IsOptionEnabled(Options.MULTIPLAYER_SLOWDOWN) ? 8 : 4;
            if (Input.InputTime % module == 0)
            {
                foreach (Creature target in Targets)
                {
                    player.room?.AddObject(new ShockWave(target.mainBodyChunk.pos, 64f, 0.05f, module));
                }
            }
        }
    }

    /// <summary>
    /// Retrieves the player's input and updates the target selector's input offset.
    /// </summary>
    /// <returns><c>true</c> if the value of <c>Input.Offset</c> has changed, <c>false</c> otherwise.</returns>
    protected virtual bool UpdateInputOffset()
    {
        Player.InputPackage input = player.GetRawInput();
        int offset = input.x + input.y;

        if (targetCursor is not null)
        {
            if (input.x != 0 || input.y != 0 || !Input.HasInput)
            {
                targetCursor.UpdateCursor(input);

                Input.HasInput = true;
            }
            return false;
        }

        if (offset == 0 && Input.Offset != offset)
            Input.Offset = 0;

        if (Input.Offset == offset && Input.HasInput)
            return false;

        Input.Offset = IsClientOptionEnabled(Options.INVERT_CLASSIC) ? -offset : offset;
        Input.HasInput = true;

        return true;
    }

    /// <summary>
    /// Attempts to select all creatures of a given template for possession.
    /// </summary>
    /// <param name="template">The creature template to search for.</param>
    /// <param name="targets">The list of valid targets with that template; May be an empty list.</param>
    /// <returns><c>true</c> if the output of <c><paramref name="targets"/></c> is greater than zero, <c>false</c> otherwise.</returns>
    protected virtual bool TrySelectNewTarget(CreatureTemplate template, out WeakList<Creature> targets)
    {
        List<Creature> allCrits = GetAllCreatures(player, template);

        targets = [];

        foreach (Creature target in allCrits)
        {
            if (IsValidSelectionTarget(target))
            {
                targets.Add(target);
            }
        }

        return targets.Count > 0;
    }

    /// <summary>
    /// Attempts to select a new target for possession based on the previous selection.
    /// </summary>
    /// <param name="lastCreature">The last creature to be selected; Can be <c>null</c>.</param>
    /// <param name="target">The new selected creature for possession; May be <c>null</c>.</param>
    /// <returns><c>true</c> if a valid creature was selected, <c>false</c> otherwise.</returns>
    protected virtual bool TrySelectNewTarget(Creature? lastCreature, out Creature? target)
    {
        int i = (lastCreature is not null ? queryCreatures.IndexOf(lastCreature) : 0) + Input.Offset;

        target = queryCreatures.ElementAtOrDefault(i);
        target ??= (Input.Offset > 0) ? queryCreatures.First() : queryCreatures.Last();

        if (!IsValidSelectionTarget(target))
        {
            Main.Logger?.LogWarning($"{target} is not a valid possession target.");

            queryCreatures.Remove(target);

            target = null;
        }

        return target is not null;
    }

    /// <summary>
    /// Retrieves all creatures in the player's room of a given template.
    /// </summary>
    /// <param name="player">The player to be tested.</param>
    /// <param name="template">The creature template to seach for.</param>
    /// <returns>A list of all creatures in the room with the given template, if any.</returns>
    public static List<Creature> GetAllCreatures(Player player, CreatureTemplate template) =>
        [.. player.room.abstractRoom.creatures
            .Select(ac => ac.realizedCreature)
            .Where(c => c is not null && GetCreatureSelector(template).Invoke(c))
        ];

    /// <summary>
    /// Retrieves the selector predicate to be used for determining creature type matches.
    /// </summary>
    /// <param name="template">The creature template to be tested.</param>
    /// <returns>A <c>Predicate</c> for evaluating if a creature is of a given type.</returns>
    public static Predicate<Creature> GetCreatureSelector(CreatureTemplate template) =>
        IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL)
            ? IsValidSelectionTarget
            : IsOptionEnabled(Options.POSSESS_ANCESTORS)
                ? c => c.Template.ancestor == template.ancestor
                : c => c.Template == template;

    /// <summary>
    /// Retrieves all valid creatures for possession within the player's possession range.
    /// </summary>
    /// <param name="player">The player to be tested.</param>
    /// <returns>A list of all possessable creatures in the room, if any.</returns>
    public static List<Creature> GetCreatures(Player player, TargetCursor? cursor)
    {
        if (player.room is null)
        {
            Main.Logger?.LogError($"{player} is not in a room; Cannot query for creatures there.");
            return [];
        }

        Vector2 pos = cursor is not null
            ? cursor.targetPos + cursor.camPos
            : player.mainBodyChunk.pos;

        List<Creature> creatures = [.. player.room.abstractRoom.creatures
            .Select(ac => ac.realizedCreature)
            .Where(c => c != player && IsValidSelectionTarget(c) && IsInPossessionRange(pos, c))
        ];

        creatures.Sort(new TargetSorter(pos));

        return creatures;
    }

    /// <summary>
    /// Retrieves the max range at which the player can possess creatures.
    /// </summary>
    /// <returns>A float determining how far a creature can be from the player to be eligible for possession.</returns>
    public static float GetPossessionRange() =>
        IsOptionEnabled(Options.WORLDWIDE_MIND_CONTROL)
            ? 9999f
            : IsClientOptionValue(Options.SELECTION_MODE, "ascension")
                ? 480f
                : 1024f;

    /// <summary>
    /// Determines if the given creature is within possession range of the player.
    /// </summary>
    /// <param name="player">The player to be tested.</param>
    /// <param name="creature">The creature to possess.</param>
    /// <returns><c>true</c> if the creature is within possession range, <c>false</c> otherwise.</returns>
    public static bool IsInPossessionRange(Vector2 pos, Creature creature) =>
        Custom.DistLess(pos, creature.mainBodyChunk.pos, GetPossessionRange());

    /// <summary>
    /// Determines if the given creature is a valid target for possession.
    /// </summary>
    /// <param name="creature">The creature to be tested.</param>
    /// <returns><c>true</c> if the creature can be possessed, <c>false</c> otherwise.</returns>
    public static bool IsValidSelectionTarget(Creature creature) =>
        !PossessionManager.IsBannedPossessionTarget(creature) && !creature.abstractCreature.controlled;

    /// <summary>
    /// Retrieves a list of all possessable creatures in the player's room.
    /// </summary>
    /// <param name="player">The player itself.</param>
    /// <returns>A list of potential targets for possession, if any.</returns>
    /// <remarks>
    /// If the player is currently using Saint's Ascension ability, the returned list will only contain
    /// one item per creature template (that is, one Yellow Lizard, one Small Centipede, etc.)
    /// </remarks>
    public static WeakList<Creature> QueryCreatures(Player player, TargetCursor? cursor) =>
        (player.monkAscension && !IsOptionEnabled(Options.FORCE_MULTITARGET_POSSESSION))
            ? [.. GetCreatures(player, cursor).Distinct(new TargetEqualityComparer())]
            : [.. GetCreatures(player, cursor)];

    /// <summary>
    /// Evaluates the value to be set for the player's <c>mushroomCounter</c> field.
    /// </summary>
    /// <param name="player">The player itself.</param>
    /// <param name="count">The value to be set.</param>
    /// <returns>The new value for the player's <c>mushroomCounter</c> field.</returns>
    /// <remarks>If the player is in a Rain Meadow lobby, this will also depend on the host's <c>meadowSlowdown</c> setting.</remarks>
    public static int SetMushroomCounter(Player player, int count) =>
        ShouldSetMushroomCounter(player, count)
            ? count
            : player.mushroomCounter;

    /// <summary>
    /// Determines if the player's <c>mushroomCounter</c> field should be updated.
    /// </summary>
    /// <param name="player">The player itself.</param>
    /// <param name="count">The value to be set.</param>
    /// <returns><c>true</c> if the value should be updated, <c>false</c> otherwise.</returns>
    /// <remarks>Has explicit support for Rain Meadow compatibility, where the host's options are also taken into account for this check.</remarks>
    public static bool ShouldSetMushroomCounter(Player player, int count) =>
        Extras.IsMultiplayer
            ? IsOptionEnabled(Options.MULTIPLAYER_SLOWDOWN) && player.mushroomCounter < count
            : player.mushroomCounter < count;

    /// <summary>
    /// Sorts a list of creatures based on their distance to a given point.
    /// </summary>
    /// <param name="playerPos">The position for measuring distance to.</param>
    /// <remarks>Edible creatures have inherently lower priority.</remarks>
    protected class TargetSorter(Vector2 playerPos) : IComparer<Creature>
    {
        public virtual int Compare(Creature x, Creature y)
        {
            float xDist = Vector2.Distance(x.mainBodyChunk.pos, playerPos);
            float yDist = Vector2.Distance(y.mainBodyChunk.pos, playerPos);

            return IgnoreCrit(x, y, xDist, yDist, out int comparison)
                ? comparison
                : xDist < yDist
                    ? -1
                    : xDist > yDist
                        ? 1
                        : 0;
        }

        private static bool IgnoreCrit(Creature x, Creature y, float distX, float distY, out int comparison, bool isRecursive = false)
        {
            bool result = x is not IPlayerEdible && y is IPlayerEdible && (distX - distY) <= 60f;

            if (result)
            {
                comparison = -1;
                return true;
            }

            if (isRecursive)
            {
                comparison = 0;
                return false;
            }

            result = IgnoreCrit(y, x, distY, distX, out comparison, isRecursive: true);

            if (result)
            {
                comparison = 1;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Compares two creatures and returns <c>true</c> if both share the same template.
    /// </summary>
    protected class TargetEqualityComparer : IEqualityComparer<Creature>
    {
        public virtual bool Equals(Creature x, Creature y) => x.Template == y.Template;
        public virtual int GetHashCode(Creature obj) => base.GetHashCode();
    }

    /// <summary>
    /// Stores the selector's input-related states and values.
    /// </summary>
    public record class TargetInput
    {
        public bool HasInput { get; set; }
        public bool LockAction { get; set; }
        public bool QueriedCursor { get; set; }

        public int InputTime { get; set; }
        public int Offset { get; set; }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue) return;

        if (disposing)
        {
            if (targetCursor is not (null or { slatedForDeletetion: true }))
            {
                targetCursor.Destroy();
                targetCursor = null;
            }

            queryCreatures?.Clear();
            Targets?.Clear();
        }

        player = null!;
        manager = null!;

        queryCreatures = null!;
        Targets = null!;
        Input = null!;

        disposedValue = true;
    }

    public void Dispose()
    {
        Main.Logger?.LogDebug($"Disposing of {nameof(TargetSelector)} from {player}!");

        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}