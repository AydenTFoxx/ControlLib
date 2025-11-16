using System;
using System.Collections.Generic;
using ModLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace ControlLib.Telekinetics;

// Notice: The following methods are not included here, but must be hooked to as well to actually prevent death:
//      Creature.Die(), Player.Destroy()
//
// The implementation for these hooks can be found in the Possession.PossessionHooks class
public static class DeathProtectionHooks
{
    private static Hook[]? manualHooks;

    public static void ApplyHooks()
    {
        IL.BigEel.JawsSnap += Extras.WrapILHook(IgnoreLeviathanBiteILHook);
        IL.BigEelAI.IUseARelationshipTracker_UpdateDynamicRelationship += Extras.WrapILHook(IgnoreProtectedPlayerILHook);

        IL.BulletDrip.Strike += Extras.WrapILHook(PreventRainDropStunILHook);
        IL.RoomRain.ThrowAroundObjects += Extras.WrapILHook(PreventRoomRainPushILHook);

        IL.WormGrass.WormGrassPatch.Update += Extras.WrapILHook(IgnoreRepulsivePlayerILHook);

        On.AbstractWorldEntity.Destroy += PreventPlayerDestructionHook;

        On.RoomRain.CreatureSmashedInGround += IgnorePlayerRainDeathHook;

        manualHooks = new Hook[2];

        manualHooks[0] = new Hook(
            typeof(Player).GetProperty(nameof(Player.windAffectiveness)).GetGetMethod(),
            IgnoreWindAffectivenessHook);

        manualHooks[1] = new Hook(
            typeof(Creature).GetProperty(nameof(Creature.WormGrassGooduckyImmune)).GetGetMethod(),
            AvoidImmunePlayerHook);

        foreach (Hook hook in manualHooks)
        {
            hook.Apply();
        }
    }

    public static void RemoveHooks()
    {
        IL.BigEel.JawsSnap -= Extras.WrapILHook(IgnoreLeviathanBiteILHook);
        IL.BigEelAI.IUseARelationshipTracker_UpdateDynamicRelationship -= Extras.WrapILHook(IgnoreProtectedPlayerILHook);

        IL.BulletDrip.Strike -= Extras.WrapILHook(PreventRainDropStunILHook);
        IL.RoomRain.ThrowAroundObjects -= Extras.WrapILHook(PreventRoomRainPushILHook);

        IL.WormGrass.WormGrassPatch.Update -= Extras.WrapILHook(IgnoreRepulsivePlayerILHook);

        On.AbstractWorldEntity.Destroy -= PreventPlayerDestructionHook;

        On.RoomRain.CreatureSmashedInGround -= IgnorePlayerRainDeathHook;

        if (manualHooks is not null)
        {
            foreach (Hook hook in manualHooks)
            {
                hook?.Undo();
            }
        }
        manualHooks = null;
    }

    /// <summary>
    /// Grants the player Worm Grass immunity when protected from death.
    /// </summary>
    private static bool AvoidImmunePlayerHook(Func<Player, bool> orig, Player self) =>
        DeathProtection.HasProtection(self) || orig.Invoke(self);

    /// <summary>
    /// Prevents the end of cycle rain from affecting Slugcat if protected.
    /// </summary>
    private static void IgnorePlayerRainDeathHook(On.RoomRain.orig_CreatureSmashedInGround orig, RoomRain self, Creature crit, float speed)
    {
        if (crit is Player player && DeathProtection.HasProtection(player)) return;

        orig.Invoke(self, crit, speed);
    }

    /// <summary>
    /// Prevents the player from being affected by wind while protected.
    /// </summary>
    private static float IgnoreWindAffectivenessHook(Func<Player, float> orig, Player self) =>
        DeathProtection.HasProtection(self)
            ? 0f
            : orig.Invoke(self);

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
    /// Prevents Leviathan bites from targeting the player, just like it can (no longer) affect the Safari mode Overseer.
    /// </summary>
    private static void IgnoreLeviathanBiteILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(static x => x.MatchStloc(7))
         .GotoNext(MoveType.After,
            static x => x.MatchIsinst(typeof(BigEel)),
            x => x.MatchBrtrue(out target)
        ).MoveAfterLabels();

        // Target: if (!(this.room.physicalObjects[j][k] is BigEel) && ...) { ... }
        //                                                         ^ HERE (Insert)

        c.Emit(OpCodes.Ldarg_0)
         .Emit(OpCodes.Ldfld, typeof(UpdatableAndDeletable).GetField(nameof(UpdatableAndDeletable.room)))
         .Emit(OpCodes.Ldfld, typeof(Room).GetField(nameof(Room.physicalObjects)))
         .Emit(OpCodes.Ldloc_S, (byte)6)
         .Emit(OpCodes.Ldelem_Ref)
         .Emit(OpCodes.Ldloc_S, (byte)7)
         .Emit(OpCodes.Callvirt, typeof(List<PhysicalObject>).GetMethod("get_Item"))
         .Emit(OpCodes.Isinst, typeof(Player))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (!(this.room.physicalObjects[j][k] is BigEel) && !DeathProtection.HasProtection(this.room.physicalObjects[j][k] as Player) && ...) { ... }
    }

    /// <summary>
    ///     Causes Leviathans to ignore players under death protection.
    /// </summary>
    private static void IgnoreProtectedPlayerILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(MoveType.After,
            static x => x.MatchCallvirt(typeof(BigEel).GetMethod(nameof(BigEel.AmIHoldingCreature))),
            x => x.MatchBrtrue(out target)
        ).MoveAfterLabels();

        // Target: if (this.eel.AmIHoldingCreature(dRelation.trackerRep.representedCreature) || dRelation.trackerRep.representedCreature.creatureTemplate.smallCreature) { ... }
        //                                                                                 ^ HERE (Insert)

        c.Emit(OpCodes.Ldarg_1)
         .Emit(OpCodes.Ldfld, typeof(RelationshipTracker.DynamicRelationship).GetField(nameof(RelationshipTracker.DynamicRelationship.trackerRep)))
         .Emit(OpCodes.Ldfld, typeof(Tracker.CreatureRepresentation).GetField(nameof(Tracker.CreatureRepresentation.representedCreature)))
         .Emit(OpCodes.Callvirt, typeof(AbstractCreature).GetProperty(nameof(AbstractCreature.realizedCreature)).GetGetMethod())
         .Emit(OpCodes.Isinst, typeof(Player))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (this.eel.AmIHoldingCreature(dRelation.trackerRep.representedCreature) || DeathProtection.HasProtection(dRelation.trackerRep.representedCreature.realizedCreature as Player) || dRelation.trackerRep.representedCreature.creatureTemplate.smallCreature) { ... }
    }

    /// <summary>
    /// Causes Worm Grass patches to fully ignore the player while protected.
    /// </summary>
    private static void IgnoreRepulsivePlayerILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(MoveType.After,
            static x => x.MatchLdloc(0),
            x => x.MatchBrfalse(out target)
        ).MoveAfterLabels();

        // Target: if (realizedCreature != null && ...) { ... }
        //                                     ^ HERE (Insert)

        c.Emit(OpCodes.Ldloc_0)
         .Emit(OpCodes.Isinst, typeof(Player))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (realizedCreature != null && !DeathProtection.HasProtection(realizedCreature as Player) && ...) { ... }
    }

    /// <summary>
    /// Prevents rain drops from stunning Slugcat while protected.
    /// </summary>
    private static void PreventRainDropStunILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(MoveType.After,
            static x => x.MatchIsinst(typeof(Creature)),
            x => x.MatchBrfalse(out target)
        ).MoveAfterLabels();

        // Target: if (collisionResult.chunk.owner is Creature) { ... }
        //                                                    ^ HERE (Append)

        c.Emit(OpCodes.Ldloc_2)
         .Emit(OpCodes.Ldfld, typeof(SharedPhysics.CollisionResult).GetField(nameof(SharedPhysics.CollisionResult.chunk)))
         .Emit(OpCodes.Callvirt, typeof(BodyChunk).GetProperty(nameof(BodyChunk.owner)).GetGetMethod())
         .Emit(OpCodes.Isinst, typeof(Player))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (collisionResult.chunk.owner is Creature && !DeathProtection.HasProtection(collisionResult.chunk.owner as Player)) { ... }
    }

    /// <summary>
    /// Prevents the rain from pushing Slugcat around if protected.
    /// </summary>
    private static void PreventRoomRainPushILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILLabel? target = null;

        c.GotoNext(MoveType.After,
            static x => x.MatchLdfld(typeof(PhysicalObject).GetField(nameof(PhysicalObject.abstractPhysicalObject))),
            static x => x.MatchLdfld(typeof(AbstractPhysicalObject).GetField(nameof(AbstractPhysicalObject.rippleLayer))),
            x => x.MatchBrtrue(out target)
        ).GotoPrev(MoveType.Before,
            static x => x.MatchLdsfld(typeof(ModManager).GetField(nameof(ModManager.Watcher)))
        ).MoveAfterLabels();

        // Target: if (!ModManager.Watcher || !this.room.game.IsStorySession || this.room.physicalObjects[i][j].abstractPhysicalObject.rippleLayer == 0) { ... }
        //             ^ HERE (Prepend)

        c.Emit(OpCodes.Ldarg_0)
         .Emit(OpCodes.Ldfld, typeof(UpdatableAndDeletable).GetField(nameof(UpdatableAndDeletable.room)))
         .Emit(OpCodes.Ldfld, typeof(Room).GetField(nameof(Room.physicalObjects)))
         .Emit(OpCodes.Ldloc_0)
         .Emit(OpCodes.Ldelem_Ref)
         .Emit(OpCodes.Ldloc_1)
         .Emit(OpCodes.Callvirt, typeof(List<PhysicalObject>).GetMethod("get_Item"))
         .Emit(OpCodes.Isinst, typeof(Player))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (!DeathProtection.HasProtection(this.room.physicalObjects[i][j] as Player) && (!ModManager.Watcher || !this.room.game.IsStorySession || this.room.physicalObjects[i][j].abstractPhysicalObject.rippleLayer == 0)) { ... }
    }
}