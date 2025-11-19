using ControlLib.Telekinetics;
using ModLib;
using ModLib.Meadow;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;

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
        IL.Creature.Update += Extras.WrapILHook(UpdatePossessedCreatureILHook);

        On.Creature.Die += CreatureDeathHook;

        On.Player.AddFood += AddPossessionTimeHook;
        On.Player.Destroy += DisposePossessionManagerHook;
        On.Player.OneWayPlacement += WarpPlayerAccessoriesHook;
        On.Player.Update += UpdatePlayerPossessionHook;

        On.UpdatableAndDeletable.Destroy += PreventCreatureDestructionHook;
    }

    /// <summary>
    /// Removes the Possession module's hooks from the game.
    /// </summary>
    public static void RemoveHooks()
    {
        IL.Creature.Update -= Extras.WrapILHook(UpdatePossessedCreatureILHook);

        On.Creature.Die -= CreatureDeathHook;

        On.Player.AddFood -= AddPossessionTimeHook;
        On.Player.Destroy -= DisposePossessionManagerHook;
        On.Player.OneWayPlacement -= WarpPlayerAccessoriesHook;
        On.Player.Update -= UpdatePlayerPossessionHook;

        On.UpdatableAndDeletable.Destroy -= PreventCreatureDestructionHook;
    }

    /// <summary>
    /// Restores the player's possession time for every full food pip acquired.
    /// </summary>
    private static void AddPossessionTimeHook(On.Player.orig_AddFood orig, Player self, int add)
    {
        orig.Invoke(self, add);

        if ((!Extras.IsMeadowEnabled || MeadowUtils.IsMine(self))
            && self.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.PossessionTime += add * 40;
        }
    }

    /// <summary>
    /// Removes any possession this creature had before death.
    /// </summary>
    private static void CreatureDeathHook(On.Creature.orig_Die orig, Creature self)
    {
        if (DeathProtection.HasProtection(self)) return;

        orig.Invoke(self);

        if ((!Extras.IsMeadowEnabled || MeadowUtils.IsMine(self))
            && self.TryGetPossession(out Player possessor)
            && possessor.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.StopCreaturePossession(self);
        }
    }

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
            || (Extras.IsMeadowEnabled && (MeadowUtils.IsGameMode(MeadowGameModes.Meadow) || !MeadowUtils.IsMine(self))))
        {
            return;
        }

        self.GetOrCreatePossessionManager().Update();
    }

    private static void WarpPlayerAccessoriesHook(On.Player.orig_OneWayPlacement orig, Player self, Vector2 pos, int playerIndex)
    {
        orig.Invoke(self, pos, playerIndex);

        if (self.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.SpritesNeedReset = true;
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
            Main.Logger?.LogWarning($"{creature} not found in a room! Setting to last protection room: {protection.room?.abstractRoom.FileName}");

            creature.room = protection.room;

            if (creature.room is null)
            {
                Main.Logger?.LogWarning($"{protection} not found in a room! Defaulting to first realized player: {creature.abstractCreature.world.game.FirstRealizedPlayer}");

                creature.room = creature.abstractCreature.world.game.FirstRealizedPlayer?.room;
            }

            if (creature.room is null)
            {
                Main.Logger?.LogWarning($"Fallback player room not found! Trying to use game camera as a last resort...");

                foreach (RoomCamera camera in creature.abstractCreature.world.game.cameras)
                {
                    creature.room = camera.room;

                    if (creature.room is not null)
                    {
                        Main.Logger?.LogInfo($"Using camera room {creature.room.abstractRoom.FileName}!");
                        break;
                    }
                }
            }

            if (creature.room is null)
            {
                Main.Logger?.LogError($"Failed to retrieve any valid room for {creature}! Destruction will not be prevented.");
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

        creature.room.AddObject(new KarmicShockwave(creature, revivePos, 80, 48f * protection.Power, 64f * protection.Power));
        creature.room.AddObject(new Explosion.ExplosionLight(revivePos, 180f * protection.Power, 1f, 80, RainWorld.GoldRGB));

        creature.room.PlaySound(SoundID.SB_A14, creature.mainBodyChunk, false, 1f, 1.25f + (Random.value * 0.5f));

        Main.Logger?.LogInfo($"{creature} was saved from destruction!");
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
            || self.room is null
            || self.room.game.rainWorld.safariMode
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
            manager.StopCreaturePossession(self);

            return false;
        }

        self.SafariControlInputUpdate(player.playerState.playerNumber);

        return true;
    }
}