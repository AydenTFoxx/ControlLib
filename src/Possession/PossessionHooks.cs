using System;
using System.Reflection;
using ControlLib.Possession.Graphics;
using ControlLib.Telekinetics;
using ModLib;
using ModLib.Meadow;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;
using Watcher;

namespace ControlLib.Possession;

/// <summary>
/// A collection of hooks for updating creatures' possession states.
/// </summary>
public static class PossessionHooks
{
    /// <summary>
    /// Applies the Possession module's hooks to the game.
    /// </summary>
    public static void ApplyHooks()
    {
        Extras.WrapAction(static () =>
        {
            IL.Creature.Update += UpdatePossessedCreatureILHook;

            IL.Watcher.LizardRotModule.Act += PerformProperNullCheckingILHook;
        });

        On.Creature.Die += CreatureDeathHook;

        On.Player.AddFood += AddPossessionTimeHook;
        On.Player.Destroy += DisposePossessionManagerHook;
        On.Player.OneWayPlacement += WarpPlayerAccessoriesHook;
        On.Player.Update += UpdatePlayerPossessionHook;

        On.UpdatableAndDeletable.Destroy += PreventCreatureDestructionHook;

        On.Watcher.LizardBlizzardModule.IsForbiddenToPull += ForbidPushingPlayerAccessoriesHook;
    }

    /// <summary>
    /// Removes the Possession module's hooks from the game.
    /// </summary>
    public static void RemoveHooks()
    {
        Extras.WrapAction(static () =>
        {
            IL.Creature.Update -= UpdatePossessedCreatureILHook;

            IL.Watcher.LizardRotModule.Act -= PerformProperNullCheckingILHook;
        });

        On.Creature.Die -= CreatureDeathHook;

        On.Player.AddFood -= AddPossessionTimeHook;
        On.Player.Destroy -= DisposePossessionManagerHook;
        On.Player.OneWayPlacement -= WarpPlayerAccessoriesHook;
        On.Player.Update -= UpdatePlayerPossessionHook;

        On.UpdatableAndDeletable.Destroy -= PreventCreatureDestructionHook;

        On.Watcher.LizardBlizzardModule.IsForbiddenToPull -= ForbidPushingPlayerAccessoriesHook;
    }

    /// <summary>
    /// Restores the player's possession time for every full food pip acquired.
    /// </summary>
    private static void AddPossessionTimeHook(On.Player.orig_AddFood orig, Player self, int add)
    {
        orig.Invoke(self, add);

        if (add <= 0 || !self.TryGetPossessionManager(out PossessionManager manager)) return;

        manager.PossessionTime += add * 40;

        if (manager.IsSofanthielSlugcat && manager.PossessionTime < manager.MaxPossessionTime)
            manager.ForceVisiblePips = 120;
    }

    /// <summary>
    /// Removes any possession this creature had before death.
    /// </summary>
    private static void CreatureDeathHook(On.Creature.orig_Die orig, Creature self)
    {
        if (DeathProtection.HasProtection(self))
        {
            if (self.grabbedBy.Count > 0)
                self.StunAllGrasps(40);
            return;
        }

        orig.Invoke(self);

        // if killed by self (or death pit) while possessed, or killed by a possessed crit -> KilledSomething(self.GetType(), manager);

        if (Extras.IsMeadowEnabled && !MeadowUtils.IsMine(self)) return;

        if (self.TryGetPossession(out Player myPossessor)
            && myPossessor.TryGetPossessionManager(out PossessionManager myManager))
        {
            if (Main.CanUnlockAchievement("sacrilege")
                && (self.killTag == self.abstractCreature || self.mainBodyChunk.pos.y < 0))
            {
                KilledSomething(self.abstractCreature.creatureTemplate.type, myManager); // Killed by self (e.g. bombs) or fell in a death pit
            }

            myManager.StopCreaturePossession(self);
        }
        else if (Main.CanUnlockAchievement("sacrilege")
            && self.killTag is not null and { realizedCreature: not null }
            && self.killTag.realizedCreature.TryGetPossession(out Player otherPossessor)
            && otherPossessor.TryGetPossessionManager(out PossessionManager otherManager))
        {
            KilledSomething(self.abstractCreature.creatureTemplate.type, otherManager); // Killed by possessed creature
        }

        static void KilledSomething(CreatureTemplate.Type victimType, PossessionManager manager)
        {
            if (manager.killedPossessionTypes is null
                || manager.killedPossessionTypes.Contains(victimType)) return;

            manager.killedPossessionTypes.Add(victimType);

            Main.Logger.LogDebug($"{manager.GetPlayer()} killed a new creature type! {victimType} ({manager.killedPossessionTypes.Count}/5)");

            if (manager.killedPossessionTypes.Count >= 5)
            {
                Main.CueAchievement("sacrilege", true);

                manager.killedPossessionTypes.Clear();
                manager.killedPossessionTypes = null;
            }
        }
    }

    /// <summary>
    /// Prevents Blizzard Lizards' blizzard shield from pushing around abstract concepts such as player UI.
    /// </summary>
    private static bool ForbidPushingPlayerAccessoriesHook(On.Watcher.LizardBlizzardModule.orig_IsForbiddenToPull orig, LizardBlizzardModule self, UpdatableAndDeletable uad) =>
        uad is not PlayerAccessory && orig.Invoke(self, uad);

    /// <summary>
    /// Prevents the destruction of creatures who are under death protection.
    /// </summary>
    private static void PreventCreatureDestructionHook(On.UpdatableAndDeletable.orig_Destroy orig, UpdatableAndDeletable self)
    {
        if (self is Creature crit and not Player && TrySaveFromDestruction(crit)) return;

        orig.Invoke(self);
    }

    /// <summary>
    /// Disposes of the player's PossessionManager when Slugcat is destroyed. Also prevents death-protected players from being destroyed.
    /// </summary>
    private static void DisposePossessionManagerHook(On.Player.orig_Destroy orig, Player self)
    {
        if (TrySaveFromDestruction(self)) return;

        orig.Invoke(self);

        if (self.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.Dispose();
        }
    }

    /// <summary>
    /// Updates the player's possession manager. If none is found, a new one is created, then updated as well.
    /// </summary>
    private static void UpdatePlayerPossessionHook(On.Player.orig_Update orig, Player self, bool eu)
    {
        orig.Invoke(self, eu);

        if (self.AI != null
            || self.room is null
            || self.room.game.rainWorld.safariMode
            || self.dead
            || self.inShortcut
            || (Extras.IsMeadowEnabled && MeadowUtils.IsGameMode(MeadowGameModes.Meadow)))
        {
            return;
        }

        PossessionManager manager = self.GetOrCreatePossessionManager();

        if (Extras.IsMeadowEnabled && !MeadowUtils.IsMine(self))
            manager.MeadowSafeUpdate();
        else
            manager.Update();
    }

    /// <summary>
    /// Marks the player's accessories for being re-created when finishing a Warp sequence.
    /// </summary>
    private static void WarpPlayerAccessoriesHook(On.Player.orig_OneWayPlacement orig, Player self, Vector2 pos, int playerIndex)
    {
        orig.Invoke(self, pos, playerIndex);

        if (self.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.SpritesNeedReset = true;
        }
    }

    /// <summary>
    ///     Prevents a silly base game bug where fully-rotted Lizards may cause a <see cref="NullReferenceException"/> when controlled via Safari mode,
    ///     freezing the game's main loop process.
    /// </summary>
    private static void PerformProperNullCheckingILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(
            static x => x.MatchLdfld(typeof(LizardRotModule).GetField(nameof(LizardRotModule.moving))),
            static x => x.MatchBrtrue(out _)
        ); // Used to skip an earlier check for this.lizard.inputWithDiagonals.Value.jmp

        c.GotoNext(
            static x => x.MatchCall(typeof(Player.InputPackage?).GetProperty(nameof(Nullable<>.Value)).GetGetMethod()),
            static x => x.MatchLdfld(typeof(Player.InputPackage).GetField(nameof(Player.InputPackage.jmp))),
            x => x.MatchBrfalse(out target)
        );

        c.GotoPrev(static x => x.MatchLdarg(0)).MoveAfterLabels(); // Cannot check for ldflda directly, so a workaround is necessary

        // Target: if (this.lizard.inputWithDiagonals.Value.jmp) { ... }
        //             ^ HERE (Prepend)

        c.Emit(OpCodes.Ldarg_0)
         .Emit(OpCodes.Ldfld, typeof(LizardRotModule).GetField(nameof(LizardRotModule.lizard), BindingFlags.Instance | BindingFlags.NonPublic))
         .EmitDelegate(HasInputWithDiagonals);

        c.Emit(OpCodes.Brfalse_S, target);

        // Result: if (HasInputWithDiagonals(this.lizard) && this.lizard.inputWithDiagonals.Value.jmp) { ... }

        static bool HasInputWithDiagonals(Lizard self)
        {
            return self.inputWithDiagonals.HasValue;
        }
    }

    /// <summary>
    /// Conditionally overrides the game's default behavior for taking control of creatures in Safari Mode.
    /// Also adds basic behaviors for validating a creature's possession state.
    /// </summary>
    private static void UpdatePossessedCreatureILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(
            MoveType.After,
            static x => x.MatchLdsfld(typeof(ModManager).GetField(nameof(ModManager.MSC))),
            x => x.MatchBrfalse(out target)
        ).MoveAfterLabels();

        // Target: if (ModManager.MSC) { this.SafariControlInputUpdate(0); }
        //                           ^ HERE (Append)

        c.Emit(OpCodes.Ldarg_0).EmitDelegate(UpdateCreaturePossession);
        c.Emit(OpCodes.Brtrue, target);

        // Result: if (ModManager.MSC && !UpdateCreaturePossession(this)) { this.SafariControlInputUpdate(0); }
    }

    /// <summary>
    /// Attempts to save the given creature from being destroyed with <see cref="UpdatableAndDeletable.Destroy"/>.
    /// Most often occurs with death pits, but should also work for Leviathan bites and the likes.
    /// </summary>
    /// <param name="creature">The creature to be saved.</param>
    /// <returns><c>true</c> if the creature has been saved (in this method call or another), <c>false</c> otherwise.</returns>
    internal static bool TrySaveFromDestruction(Creature creature)
    {
        if (creature.inShortcut
            || !DeathProtection.TryGetProtection(creature, out DeathProtection protection)
            || !protection.SafePos.HasValue) return false;

        if (protection.SaveCooldown > 0) return true;

        if (creature.room is null)
        {
            creature.room = creature.abstractCreature.world.GetAbstractRoom(protection.SafePos.Value.room)?.realizedRoom;

            creature.room ??= protection.room;
            creature.room ??= creature.abstractCreature.Room?.realizedRoom;

            if (creature.room is null)
            {
                Main.Logger.LogWarning($"Could not retrieve a room for {creature}; Protection will not be avoided.");
                return false;
            }
        }

        Vector2 revivePos = creature.room.MiddleOfTile(protection.SafePos.Value);

        if (creature is Player player)
        {
            player.SuperHardSetPosition(revivePos);

            player.animation = Player.AnimationIndex.StandUp;
            player.allowRoll = 0;
            player.rollCounter = 0;
            player.rollDirection = 0;
        }
        else
        {
            foreach (BodyChunk bodyChunk in creature.bodyChunks)
            {
                bodyChunk.HardSetPosition(revivePos);
            }
        }

        Vector2 bodyVel = new(0f, 8f + creature.room.gravity);
        foreach (BodyChunk bodyChunk in creature.bodyChunks)
        {
            bodyChunk.vel = bodyVel;
        }

        if (creature.grabbedBy.Count > 0)
            creature.StunAllGrasps(80);

        creature.room.AddObject(new KarmicShockwave(creature, revivePos, 80, 48f * protection.Power, 64f * protection.Power));
        creature.room.AddObject(new Explosion.ExplosionLight(revivePos, 180f * protection.Power, 1f, 80, RainWorld.GoldRGB));

        creature.room.PlaySound(SoundID.SB_A14, creature.mainBodyChunk, false, 1f, 1.25f + (UnityEngine.Random.value * 0.5f));

        if (creature is Player && !creature.room.game.IsArenaSession)
            Main.CueAchievement("saving_grace");

        Main.Logger.LogInfo($"{creature} was saved from destruction!");
        return true;
    }

    /// <summary>
    /// Updates the creature's possession state. If the possession is no longer valid, it is removed instead.
    /// </summary>
    /// <param name="self">The creature itself.</param>
    /// <returns><c>true</c> if the game's default behavior was overriden, <c>false</c> otherwise.</returns>
    private static bool UpdateCreaturePossession(Creature self)
    {
        if (self is Overseer or { room: null }
            || self.room.game.rainWorld.safariMode
            || !self.abstractCreature.controlled) return false;

        if (!self.TryGetPossession(out Player possessor)
            || !possessor.TryGetPossessionManager(out PossessionManager manager))
        {
            self.UpdateCachedPossession();
            self.abstractCreature.controlled = false;

            return false;
        }

        if (Extras.IsMeadowEnabled && !MeadowUtils.IsMine(self)) return false;

        if (!manager.HasCreaturePossession(self) || !manager.IsPossessionValid(self))
        {
            manager.StopCreaturePossession(self);

            return false;
        }

        self.SafariControlInputUpdate(possessor.playerState.playerNumber);

        return true;
    }
}