using System.Collections.Generic;
using System.Linq;
using ControlLib.Telekinetics;
using ModLib;
using ModLib.Collections;
using ModLib.Input;
using ModLib.Meadow;
using ModLib.Options;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using UnityEngine;

namespace ControlLib.Possession;

/// <summary>
/// A collection of hooks for updating creatures' possession states.
/// </summary>
public static class PossessionHooks
{
    private static readonly WeakList<Player> RevivedPlayers = [];

    /// <summary>
    /// Applies the Possession module's hooks to the game.
    /// </summary>
    public static void ApplyHooks()
    {
        IL.Creature.Update += Extras.WrapILHook(UpdatePossessedCreatureILHook);

        On.AbstractWorldEntity.Destroy += PreventPlayerDestructionHook;

        On.Creature.Die += CreatureDeathHook;

        On.Player.AddFood += AddPossessionTimeHook;
        On.Player.Destroy += DisposePossessionManagerHook;
        On.Player.OneWayPlacement += WarpPlayerAccessoriesHook;
        On.Player.Update += UpdatePlayerPossessionHook;
    }

    /// <summary>
    /// Removes the Possession module's hooks from the game.
    /// </summary>
    public static void RemoveHooks()
    {
        IL.Creature.Update -= Extras.WrapILHook(UpdatePossessedCreatureILHook);

        On.AbstractWorldEntity.Destroy -= PreventPlayerDestructionHook;

        On.Creature.Die -= CreatureDeathHook;

        On.Player.AddFood -= AddPossessionTimeHook;
        On.Player.Destroy -= DisposePossessionManagerHook;
        On.Player.OneWayPlacement -= WarpPlayerAccessoriesHook;
        On.Player.Update -= UpdatePlayerPossessionHook;
    }

    /// <summary>
    /// Restores the player's possession time for every full food pip acquired.
    /// </summary>
    private static void AddPossessionTimeHook(On.Player.orig_AddFood orig, Player self, int add)
    {
        orig.Invoke(self, add);

        if (Extras.IsMeadowEnabled && !MeadowUtils.IsMine(self)) return;

        if (self.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.PossessionTime += add * 40;
        }
    }

    /// <summary>
    /// Removes any possession this creature had before death.
    /// </summary>
    private static void CreatureDeathHook(On.Creature.orig_Die orig, Creature self)
    {
        if (self is Player player && DeathProtection.TryGetProtection(player, out _)) return;

        orig.Invoke(self);

        if ((!Extras.IsMeadowEnabled || MeadowUtils.IsMine(self))
            && self.TryGetPossession(out Player possessor)
            && possessor.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.StopPossession(self);
        }
    }

    /// <summary>
    /// Disposes of the player's PossessionManager when Slugcat is destroyed.
    /// </summary>
    private static void DisposePossessionManagerHook(On.Player.orig_Destroy orig, Player self)
    {
        if (TrySaveFromDestruction(self)) return;

        orig.Invoke(self);

        if (self.TryGetPossessionManager(out PossessionManager myManager))
        {
            myManager.Dispose();
        }
    }

    /// <summary>
    /// Prevents the player's abstract representation from being destroyed while death-immune.
    /// </summary>
    private static void PreventPlayerDestructionHook(On.AbstractWorldEntity.orig_Destroy orig, AbstractWorldEntity self)
    {
        if (self is AbstractCreature abstractCreature
            && abstractCreature.realizedCreature is Player player
            && DeathProtection.TryGetProtection(player, out _)) return;

        orig.Invoke(self);
    }

    /// <summary>
    /// Updates the player's possession manager. If none is found, a new one is created, then updated as well.
    /// </summary>
    private static void UpdatePlayerPossessionHook(On.Player.orig_Update orig, Player self, bool eu)
    {
        if (self.dangerGrasp is not null
            && self.dangerGraspTime < 60
            && self.room is not null
            && (self.room.game.IsArenaSession || (self.room.game.GetStorySession?.saveState.deathPersistentSaveData.reinforcedKarma ?? false))
            && OptionUtils.IsOptionEnabled(Options.KINETIC_ABILITIES)
            && !RevivedPlayers.Contains(self)
            && (self.dead || self.IsKeyDown(Keybinds.MIND_BLAST, true))
            && (!Extras.IsMeadowEnabled || MeadowUtils.IsMine(self))) // Welcome to conditional hell
        {
            if (self.room.game.session is StoryGameSession storySession)
            {
                storySession.saveState.deathPersistentSaveData.reinforcedKarma = false;

                foreach (RoomCamera camera in self.room.game.cameras)
                {
                    camera.hud?.karmaMeter.reinforceAnimation = 1;
                    camera.hud?.karmaMeter.symbolDirty = true;
                }
            }

            self.room.AddObject(new FadingMeltLights(self.room));
            self.room.AddObject(new KarmicShockwave(self, self.dangerGrasp.grabbedChunk.pos, 40, 40f, 80f));

            self.AllGraspsLetGoOfThisObject(true);

            List<Creature> dangerousCrits = [.. self.room.updateList.OfType<Creature>().Where(c => c.abstractCreature.abstractAI?.RealAI?.DynamicRelationship(self.abstractCreature).GoForKill ?? false)];

            for (int i = 0; i < dangerousCrits.Count; i++)
            {
                Creature crit = dangerousCrits[i];

                if (crit == self) continue; // How in the world would a Slugcat be dangerous? I dunno, but you can never trust a modder's ability to surprise ya and break your code

                float distFactor = -(Vector2.Distance(crit.mainBodyChunk.pos, self.dangerGrasp.grabbedChunk.pos) - 480f);

                if (distFactor <= 0f) continue;

                crit.Deafen((int)(distFactor * 1.25f));
                crit.Blind((int)(distFactor * 0.75f));

                crit.Violence(self.dead ? crit.mainBodyChunk : self.dangerGrasp.grabbedChunk, Custom.DirVec(self.dangerGrasp.grabbedChunk.pos, crit.mainBodyChunk.pos) * (distFactor * 0.5f), crit.mainBodyChunk, null, Creature.DamageType.Explosion, distFactor * 0.01f * Random.Range(1f, 2f), distFactor);
            }

            self.stun = 0;

            DeathProtection.CreateInstance(self, 40 * Random.Range(8, 10));

            RevivedPlayers.Add(self);
        }

        orig.Invoke(self, eu);

        if (self.dead
            || self.inShortcut
            || (Extras.IsMeadowEnabled && (MeadowUtils.IsGameMode(MeadowGameModes.Meadow) || !MeadowUtils.IsMine(self))))
        {
            return;
        }

        PossessionManager manager = self.GetOrCreatePossessionManager();

        manager.Update();
    }

    private static void WarpPlayerAccessoriesHook(On.Player.orig_OneWayPlacement orig, Player self, Vector2 pos, int playerIndex)
    {
        orig.Invoke(self, pos, playerIndex);

        if (self.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.ResetAccessories();
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

        c.Emit(OpCodes.Ldarg_0).Emit(OpCodes.Ldc_I4_0).EmitDelegate(UpdateCreaturePossession);
        c.Emit(OpCodes.Brtrue, target);

        // Result: if (ModManager.MSC && !UpdateCreaturePossession(this, false)) { this.SafariControlInputUpdate(0); }
    }

    /// <summary>
    /// Attempts to save the player from being destroyed with <see cref="Player.Destroy"/>.
    /// Most often occurs with death pits, but should also work for Leviathan bites and the likes.
    /// </summary>
    /// <param name="player">The player to be saved.</param>
    /// <returns><c>true</c> if the player has been saved (in this method call or another), <c>false</c> otherwise.</returns>
    private static bool TrySaveFromDestruction(Player player)
    {
        if (player.room is null
            || !DeathProtection.TryGetProtection(player, out DeathProtection protection)
            || !protection.SafePos.HasValue) return false;

        if (protection.SaveCooldown > 0) return true;

        Vector2 revivePos = player.room.MiddleOfTile(protection.SafePos.Value);

        player.SuperHardSetPosition(revivePos);

        Vector2 bodyVel = new(0f, 8f + player.room.gravity);
        foreach (BodyChunk bodyChunk in player.bodyChunks)
        {
            bodyChunk.vel = bodyVel;
        }

        player.room.AddObject(new KarmicShockwave(player, revivePos, 80, 48, 64));
        player.room.AddObject(new Explosion.ExplosionLight(revivePos, 180f * protection.Power, 1f, 80, RainWorld.GoldRGB));

        player.room.PlaySound(SoundID.SB_A14, player.mainBodyChunk, false, 1f, 1.25f + (Random.value * 0.5f));

        Main.Logger?.LogInfo($"{player} was saved from destruction!");
        return true;
    }

    /// <summary>
    /// Updates the creature's possession state. If the possession is no longer valid, it is removed instead.
    /// </summary>
    /// <param name="self">The creature itself.</param>
    /// <param name="isRecursive">Whether this method call is recursive (i.e. called by itself a second time).</param>
    /// <returns><c>true</c> if the game's default behavior was overriden, <c>false</c> otherwise.</returns>
    private static bool UpdateCreaturePossession(Creature self, bool isRecursive)
    {
        if (self is Player or Overseer
            || !self.abstractCreature.controlled
            || (Extras.IsMeadowEnabled && !MeadowUtils.IsMine(self))) return false;

        if (!self.TryGetPossession(out Player player)
            || !player.TryGetPossessionManager(out PossessionManager manager))
        {
            self.UpdateCachedPossession();

            return !isRecursive && UpdateCreaturePossession(self, true);
        }

        if (!manager.HasPossession(self) || !manager.IsPossessionValid(self))
        {
            manager.StopPossession(self);

            return false;
        }

        self.SafariControlInputUpdate(player.playerState.playerNumber);

        return true;
    }
}