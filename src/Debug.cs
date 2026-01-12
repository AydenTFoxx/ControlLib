using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Possessions.Enums;
using Possessions.Possession;
using Possessions.Telekinetics;
using RWCustom;

namespace Possessions;

/// <summary>
///     Debug-only methods for testing purposes, meant to be called using the Dev Console mod.
/// </summary>
/// <remarks>
///     To use a given method in-game, call the following command:<br/>
///     <code>
///     > invoke ControlLib.Debug.{MethodNameHere}
///     </code>
///     Note most methods also require the player's ID or index as their first argument, which is <c>0</c> in singleplayer.
/// </remarks>
public static class Debug
{
    /// <summary>
    ///     Represents the success or failure of an operation and its returning value, if any is present.
    /// </summary>
    /// <param name="success">Whether or not the operation was successful.</param>
    /// <param name="value">The resulting value of the operation, if any.</param>
    public readonly struct Result(bool success, object? value) : IEquatable<Result>
    {
        /// <summary>
        ///     A <see cref="Result"/> instance representing a success with no resulting value.
        ///     This field is read-only.
        /// </summary>
        public static readonly Result GenericSuccess = new(true, null);

        /// <summary>
        ///     A <see cref="Result"/> instance representing a failure with no resulting value.
        ///     This field is read-only.
        /// </summary>
        public static readonly Result GenericFailure = new(false, null);

        /// <summary>
        ///     Whether or not the performed operation succeeded in its execution.
        /// </summary>
        public bool Success { get; } = success;

        /// <summary>
        ///     The resulting value of the performed operation, if any.
        /// </summary>
        public object? Value { get; } = value;

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Result other) => other.Success == Success && other.Value == Value;

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is Result other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Success.GetHashCode() + (Value is not null ? Value.GetHashCode() : 0);

        /// <summary>
        ///     Returns a string representing the resulting values stored by this instance.
        /// </summary>
        /// <returns>A string representing the values stored by this instance.</returns>
        public override string ToString() => $"[{(Success ? "SUCCESS" : "FAIL")}] ({Value})";

        public static bool operator ==(Result x, Result y)
        {
            return x.Equals(y);
        }

        public static bool operator !=(Result x, Result y)
        {
            return !x.Equals(y);
        }
    }

    /// <summary>
    ///     Represents the invoking syntax of a given command method.
    /// </summary>
    /// <param name="name">The name of the method itself.</param>
    /// <param name="args">
    ///     The required or optional arguments, if any.
    ///     Optional arguments are identified by a <c>?</c> suffix.
    /// </param>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CommandSyntaxAttribute(string name, params string[] args) : Attribute
    {
        public string GetFullInvocation() => $"invoke ControlLib.Debug.{this}";

        public override string ToString()
        {
            StringBuilder builder = new(name);

            foreach (string arg in args)
            {
                if (arg.EndsWith("?"))
                    builder.Append($" [{arg.TrimEnd('?')}]");
                else
                    builder.Append($" <{arg}>");
            }

            return builder.ToString();
        }
    }

    private static readonly Result GameNotFoundResult = new(false, "Game instance could not be found.");
    private static readonly Result InvalidCreatureIDResult = new(false, "Creature ID is not valid.");
    private static readonly Result NoTargetFoundResult = new(false, "No valid target was found.");

    /// <summary>
    ///     Returns a list of all registered methods whose command syntax is known at runtime,
    ///     with their name and parameters, if any.
    /// </summary>
    [CommandSyntax(nameof(Help), "commandName?")]
    public static Result Help()
    {
        StringBuilder builder = new(Environment.NewLine);

        foreach (MethodInfo method in typeof(Debug).GetMethods())
        {
            if (Attribute.GetCustomAttribute(method, typeof(CommandSyntaxAttribute)) is not CommandSyntaxAttribute syntax) continue;

            builder.AppendLine($"  {syntax}");
        }

        return new Result(true, builder.ToString());
    }

    /// <summary>
    ///     Returns the full invocation of a given method.
    /// </summary>
    /// <param name="commandName">The method name to be searched.</param>
    public static Result Help(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            return new Result(false, "Command name is not valid.");
        }

        bool success = false;
        string message;

        MethodInfo? method = typeof(Debug).GetMethods().FirstOrDefault(m => m.Name == commandName && Attribute.IsDefined(m, typeof(CommandSyntaxAttribute)));

        if (method is null)
        {
            message = $"Could not find a command with name {commandName}.";
        }
        else if (Attribute.GetCustomAttribute(method, typeof(CommandSyntaxAttribute)) is CommandSyntaxAttribute syntax)
        {
            message = $"Usage: {syntax.GetFullInvocation()}";
            success = true;
        }
        else
        {
            message = $"{method} is not a valid command type.";
        }

        return new Result(success, message);
    }

    /// <summary>
    ///     Creates a <see cref="FadingMeltLights"/> instance at the given player's room.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the new <see cref="FadingMeltLights"/> instance.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(CreateMeltLights), "playerIndex")]
    public static Result CreateMeltLights(string playerIndex)
    {
        if (!ValidatePlayerIndex(playerIndex, out Player? player, out Result validationResult))
            return validationResult;

        FadingMeltLights fadingMeltLights = new(player.room);

        player.room.AddObject(fadingMeltLights);

        return new Result(true, fadingMeltLights);
    }

    /// <summary>
    ///     Creates a detached <see cref="MindBlast"/> instance at the given player's position.
    ///     The created instance does not require inputs, but inherits its power from the player's <see cref="PossessionManager"/> instance, if one is found.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the new <see cref="MindBlast"/> instance.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(CreateMindBlast), "playerIndex")]
    public static Result CreateMindBlast(string playerIndex)
    {
        if (!ValidatePlayerIndex(playerIndex, out Player? player, out Result validationResult))
            return validationResult;

        MindBlast? mindBlast = MindBlast.CreateInstance(player, null, true);

        mindBlast.pos = player.mainBodyChunk.pos;

        return new Result(true, mindBlast);
    }

    /// <summary>
    ///     Explodes the creature with the given ID, using a <see cref="ScavengerBomb"/> as the object type.
    /// </summary>
    /// <param name="creatureID">The ID of the creature to target.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the targeted creature and the explosion type;
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(ExplodeCreature), "creatureID", "isSingularity?")]
    public static Result ExplodeCreature(string creatureID) => ExplodeCreature(creatureID, bool.FalseString);

    /// <summary>
    ///     Explodes the creature with the given ID, using a <see cref="ScavengerBomb"/> or <see cref="MoreSlugcats.SingularityBomb"/> as the object type.
    /// </summary>
    /// <param name="creatureID">The ID of the creature to target.</param>
    /// <param name="isSingularity">If true, the object type will be a Singularity Bomb. Otherwise, it will be a Scavenger Bomb.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the targeted creature and the explosion type;
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    public static Result ExplodeCreature(string creatureID, string isSingularity)
    {
        if (GetMainLoopProcess() is not RainWorldGame game) return GameNotFoundResult;

        if (!TryParseInt(creatureID, out int inputID, allowSigns: true))
            return InvalidCreatureIDResult;

        EntityID targetID = new(-1, inputID);

        if (!bool.TryParse(isSingularity, out bool spawnSingularity))
            spawnSingularity = false;

        AbstractCreature? target = game.world.abstractRooms.SelectMany(ar => ar.creatures).FirstOrDefault(ac => ac.ID == targetID);

        if (target is null || target.realizedCreature is null)
            return NoTargetFoundResult;

        ExplosionManager.ExplodeCreature(target.realizedCreature, spawnSingularity ? DLCSharedEnums.AbstractObjectType.SingularityBomb : AbstractPhysicalObject.AbstractObjectType.ScavengerBomb);

        return new Result(true, $"CRIT: {target} | BOMB: {(spawnSingularity ? "Singularity" : "ScavengerBomb")}");
    }

    /// <summary>
    ///     Explodes the given player using a <see cref="ScavengerBomb"/> as the object type.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the targeted player and the explosion type;
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(ExplodePlayer), "playerIndex", "isSingularity?")]
    public static Result ExplodePlayer(string playerIndex) => ExplodePlayer(playerIndex, bool.FalseString);

    /// <summary>
    ///     Explodes the given player using either a <see cref="ScavengerBomb"/> or <see cref="MoreSlugcats.SingularityBomb"/> as the object type.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <param name="isSingularity">If true, the object type will be a Singularity Bomb. Otherwise, it will be a Scavenger Bomb.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the targeted player and the explosion type;
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    public static Result ExplodePlayer(string playerIndex, string isSingularity)
    {
        if (!ValidatePlayerIndex(playerIndex, out Player player, out Result validationResult))
            return validationResult;

        if (!bool.TryParse(isSingularity, out bool spawnSingularity))
            spawnSingularity = false;

        ExplosionManager.ExplodeCreature(player, spawnSingularity ? DLCSharedEnums.AbstractObjectType.SingularityBomb : AbstractPhysicalObject.AbstractObjectType.ScavengerBomb);

        return new Result(true, $"PLAYER: {player} | BOMB: {(spawnSingularity ? "Singularity" : "ScavengerBomb")}");
    }

    /// <summary>
    ///     Explodes the given position in the player's room using a <see cref="ScavengerBomb"/> as the object type.
    /// </summary>
    /// <param name="playerIndex">The index of the player whose room will be targeted.</param>
    /// <param name="x">The X position to target.</param>
    /// <param name="y">The Y position to target.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the targeted position and the explosion type;
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(ExplodePos), "playerIndex", "isSingularity?")]
    public static Result ExplodePos(string playerIndex, string x, string y) => ExplodePos(playerIndex, x, y, bool.FalseString);

    /// <summary>
    ///     Explodes the given position in the player's room using either a <see cref="ScavengerBomb"/> or <see cref="MoreSlugcats.SingularityBomb"/> as the object type.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <param name="x">The X position to target.</param>
    /// <param name="y">The Y position to target.</param>
    /// <param name="isSingularity">If true, the object type will be a Singularity Bomb. Otherwise, it will be a Scavenger Bomb.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the targeted player and the explosion type;
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    public static Result ExplodePos(string playerIndex, string x, string y, string isSingularity)
    {
        if (!ValidatePlayerIndex(playerIndex, out Player player, out Result validationResult))
            return validationResult;

        if (!TryParseInt(x, out int targetX) || !TryParseInt(y, out int targetY))
            return new Result(false, "Target position is not valid.");

        if (!bool.TryParse(isSingularity, out bool spawnSingularity))
            spawnSingularity = false;

        WorldCoordinate pos = player.room.GetWorldCoordinate(new IntVector2(targetX, targetY));

        ExplosionManager.ExplodePos(player, player.room, pos, spawnSingularity ? DLCSharedEnums.AbstractObjectType.SingularityBomb : AbstractPhysicalObject.AbstractObjectType.ScavengerBomb);

        return new Result(true, $"POS: {pos} | BOMB: {(spawnSingularity ? "Singularity" : "ScavengerBomb")}");
    }

    [CommandSyntax(nameof(GetPossessionManager), "playerIndex")]
    public static Result GetPossessionManager(string playerIndex)
    {
        return !ValidatePlayerIndex(playerIndex, out Player? player, out Result validationResult)
            ? validationResult
            : !player.TryGetPossessionManager(out PossessionManager manager)
                ? new Result(false, "Player has no PossessionManager instance.")
                : new Result(true, manager);
    }

    /// <summary>
    ///     Forces the given player's <see cref="PossessionManager"/> instance to be disposed and re-created.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the new <see cref="PossessionManager"/> instance.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(ResetPossessionManager), "playerIndex")]
    public static Result ResetPossessionManager(string playerIndex)
    {
        if (!ValidatePlayerIndex(playerIndex, out Player? player, out Result validationResult))
            return validationResult;

        if (player.TryGetPossessionManager(out PossessionManager manager))
        {
            if (manager.GetPlayer() != player)
            {
                Main.Logger.LogWarning($"PossessionManager owner mismatch! Manager is bound to: {player}; But owner is: {manager.GetPlayer()}! Removing bound manager from {player}.");

                player.RemovePossessionManager();
            }

            manager.Dispose();
        }

        return new Result(true, player.GetOrCreatePossessionManager());
    }

    /// <summary>
    ///     Initializes a new creature possession for the given player, targeting the nearest creature in the room.
    /// </summary>
    /// <param name="playerIndex">The index of the player who will start the possession.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing whether or not the creature's abstract representation is being controlled.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(StartCreaturePossession), "playerIndex", "creatureID?")]
    public static Result StartCreaturePossession(string playerIndex) => StartCreaturePossession(playerIndex, null);

    /// <summary>
    ///     Initializes a new creature possession for the given player, using the specified creature ID if possible.
    /// </summary>
    /// <param name="playerIndex">The index of the player who will start the possession.</param>
    /// <param name="creatureID">The ID of the creature to be targeted.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing whether or not the creature's abstract representation is being controlled.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    public static Result StartCreaturePossession(string playerIndex, string? creatureID)
    {
        if (!ValidatePlayerIndex(playerIndex, out Player? player, out Result validationResult))
            return validationResult;

        if (!player.TryGetPossessionManager(out PossessionManager manager))
            return new Result(false, "Player has no PossessionManager instance.");

        Creature? target = null;

        if (TryParseInt(creatureID, out int inputID, allowSigns: true))
        {
            EntityID targetID = new(-1, inputID);

            target = player.room.physicalObjects.SelectMany(static list => list).OfType<Creature>().FirstOrDefault(c => c.abstractCreature.ID == targetID);
        }
        else
        {
            target = player.room.physicalObjects.OfType<Creature>().OrderBy(static crit => crit, new TargetSelector.TargetSorter(player.mainBodyChunk.pos)).FirstOrDefault();
        }

        if (target is null)
            return NoTargetFoundResult;

        if (target.TryGetPossession(out Player other)
            && other.TryGetPossessionManager(out PossessionManager otherManager))
        {
            otherManager.StopCreaturePossession(target);
        }

        manager.StartCreaturePossession(target);

        return target.abstractCreature.controlled
            ? new Result(true, target)
            : new Result(false, $"Failed to possess {target}! See logs for details.");
    }

    /// <summary>
    ///     Initializes a new item possession for the given player, without targeting their held items.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the new <see cref="ObjectController"/> instance.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(StartItemPossession), "playerIndex", "itemID?")]
    public static Result StartItemPossession(string playerIndex) => StartItemPossession(playerIndex, null);

    /// <summary>
    ///     Initializes a new item possession for the given player, using the specified grasp if possible.
    /// </summary>
    /// <param name="playerIndex">The index of the player to be targeted.</param>
    /// <param name="itemID">The ID of the item to possess, or <c>null</c> to select a nearby carryable item instead.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the new <see cref="ObjectController"/> instance.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    public static Result StartItemPossession(string playerIndex, string? itemID)
    {
        if (!ValidatePlayerIndex(playerIndex, out Player? player, out Result validationResult))
            return validationResult;

        PhysicalObject? targetItem = null;

        if (TryParseInt(itemID, out int inputID, allowSigns: true))
        {
            EntityID targetID = new(-1, inputID);

            targetItem = player.room.physicalObjects.SelectMany(static obj => obj).FirstOrDefault(p => p.abstractPhysicalObject.ID == targetID);

            if (targetItem is not null and { grabbedBy.Count: > 0 })
            {
                targetItem.AllGraspsLetGoOfThisObject(true);
            }
        }

        if (targetItem is null)
        {
            IEnumerable<PhysicalObject> eligibleObjects = player.room.physicalObjects.SelectMany(static obj => obj).OrderBy(static item => item, new TargetSelector.TargetSorter(player.mainBodyChunk.pos)).Where(obj => obj.grabbedBy.Count == 0 && Custom.DistLess(player.mainBodyChunk.pos, obj.firstChunk.pos, TargetSelector.GetPossessionRange() * 3f));

            targetItem = eligibleObjects.OfType<PlayerCarryableItem>().FirstOrDefault();

            targetItem ??= eligibleObjects.OfType<Creature>().FirstOrDefault(c => c is Player plr ? plr.onBack is null : player.IsCreatureLegalToHoldWithoutStun(c));
        }

        if (targetItem is null)
            return NoTargetFoundResult;

        if (targetItem is Spear spear)
        {
            if (spear is not ExplosiveSpear)
                spear.abstractSpear.stuckInWallCycles = 0;

            spear.ChangeMode(Weapon.Mode.Free);
        }

        Main.Logger.LogDebug($"Selected target is: {targetItem}");

        AbstractPhysicalObject abstractController = new(
            player.abstractCreature.world,
            AbstractObjectTypes.ObjectController,
            null,
            player.abstractCreature.pos,
            player.room.game.GetNewID());

        abstractController.RealizeInRoom();

        if (abstractController.realizedObject is ObjectController controller)
        {
            controller.Target = targetItem;

            player.SlugcatGrab(controller, player.grasps[0] is null ? 0 : 1);

            if (player.TryGetPossessionManager(out PossessionManager manager))
            {
                manager.StartItemPossession(targetItem);
            }

            return new Result(true, controller);
        }
        else
        {
            abstractController.Destroy();
            abstractController.realizedObject?.Destroy();

            Main.Logger.LogWarning("Failed to realize controller object, aborting operation.");

            return Result.GenericFailure;
        }
    }

    /// <summary>
    ///     Stops all possessions of all players, and attempts to fix "ghost possessions" where a creature is controlled without having a possession.
    /// </summary>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance containing the number of "ghost" possessions found, or <c>null</c> if playing Safari mode.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(StopAllPossessions))]
    public static Result StopAllPossessions()
    {
        if (GetMainLoopProcess() is not RainWorldGame game) return GameNotFoundResult;

        foreach (Player player in game.Players.Select(ac => (Player)ac.realizedCreature))
        {
            if (player.TryGetPossessionManager(out PossessionManager manager))
            {
                manager.ResetAllPossessions();
            }
        }

        if (!game.rainWorld.safariMode)
        {
            List<AbstractCreature> controlledCrits = [.. game.world.abstractRooms.SelectMany(ar => ar.creatures).Where(ac => ac.controlled)];

            if (controlledCrits.Count > 0)
            {
                Main.Logger.LogWarning($"Found controlled creatures with no controlling player! Affected crits are: {PossessionManager.FormatPossessions(controlledCrits)}.");

                foreach (AbstractCreature creature in controlledCrits)
                {
                    Main.Logger.LogInfo($"Setting controlled field of {creature} to false.");

                    creature.controlled = false;

                    creature.realizedCreature?.UpdateCachedPossession();
                }
            }

            return new Result(true, controlledCrits.Count);
        }

        return Result.GenericSuccess;
    }

    /// <summary>
    ///     Toggles protection against all harm and death causes for the given player.
    /// </summary>
    /// <param name="creatureID">The ID of the creature to target.</param>
    /// <returns>
    ///     If successful, a <see cref="Result"/> instance determining whether or not the given creature is being protected.
    ///     Otherwise, a <see cref="Result"/> object with a string detailing why the method call failed.
    /// </returns>
    [CommandSyntax(nameof(ToggleDeathProtection), "creatureID")]
    public static Result ToggleDeathProtection(string creatureID)
    {
        if (GetMainLoopProcess() is not RainWorldGame game) return GameNotFoundResult;

        if (!TryParseInt(creatureID, out int inputID, allowSigns: true))
            return InvalidCreatureIDResult;

        EntityID targetID = new(-1, inputID);

        Creature? target = game.world.abstractRooms.SelectMany(ar => ar.creatures).FirstOrDefault(ac => ac.ID == targetID)?.realizedCreature;

        if (target is null)
            return NoTargetFoundResult;

        if (DeathProtection.TryGetProtection(target, out DeathProtection protection))
        {
            protection.Destroy();
        }
        else
        {
            DeathProtection.CreateInstance(target, DeathProtection.NullCondition);
        }

        return new Result(true, protection is null);
    }

    /// <summary>
    ///     Retrieves the current <see cref="MainLoopProcess"/> instance of the game, if any is present.
    /// </summary>
    /// <returns>The <see cref="MainLoopProcess"/> instance of the game, or <c>null</c> if none is found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static MainLoopProcess? GetMainLoopProcess() => UnityEngine.Object.FindObjectOfType<RainWorld>()?.processManager?.currentMainLoop;

    /// <summary>
    ///     Attempts to parse a given string into a signed integer value.
    /// </summary>
    /// <param name="s">The string to be parsed.</param>
    /// <param name="result">The resulting value.</param>
    /// <returns><c>true</c> if the conversion was successful, <c>false</c> otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseInt(string? s, out int result, bool allowSigns = false) =>
        int.TryParse(s, allowSigns ? NumberStyles.Integer : NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out result);

    /// <summary>
    ///     Retrieves the player instance with the given index from the game.
    /// </summary>
    /// <param name="playerIndex">The index number to be searched.</param>
    /// <param name="player">The player instance, if any is found.</param>
    /// <returns><c>true</c> if a valid player instance was found, <c>false</c> otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidatePlayerIndex(string playerIndex, out Player player, out Result result)
    {
        bool isIndexValid = TryParseInt(playerIndex, out int playerNumber);

        if (isIndexValid && GetMainLoopProcess() is RainWorldGame game)
        {
            player = (Player)game.Players.ElementAtOrDefault(playerNumber)?.realizedCreature!;
            result = player is null
                ? new Result(false, $"Player {playerIndex} could not be found.")
                : player.room is null
                    ? new Result(false, $"Player {playerIndex} is not in a realized room.")
                    : Result.GenericSuccess;
        }
        else
        {
            Main.Logger.LogWarning($"Failed to retrieve the player with index {playerIndex}: {(isIndexValid ? "Game instance not found" : "Not a valid positive number")}.");

            player = null!;
            result = isIndexValid ? GameNotFoundResult : new Result(false, "Index number is not valid.");
        }

        return result.Success;
    }
}