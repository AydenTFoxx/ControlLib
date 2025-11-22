using System;
using System.Collections.Generic;
using System.Linq;
using ModLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace ControlLib.Telekinetics;

// Notice: The following methods are not included here, but must be hooked to as well to actually prevent death:
//      Creature.Die(), UpdatableAndDeletable.Destroy(), Player.Destroy()
//
// The implementation for these hooks can be found in the Possession.PossessionHooks class
public static class DeathProtectionHooks
{
    private static Hook[]? manualHooks;

    public static void ApplyHooks()
    {
        IL.BigEel.JawsSnap += Extras.WrapILHook(IgnoreLeviathanBiteILHook);
        IL.BigEelAI.IUseARelationshipTracker_UpdateDynamicRelationship += Extras.WrapILHook(IgnoreProtectedCreatureILHook);

        IL.BulletDrip.Strike += Extras.WrapILHook(PreventRainDropStunILHook);
        IL.RoomRain.ThrowAroundObjects += Extras.WrapILHook(PreventRoomRainPushILHook);

        IL.WormGrass.WormGrassPatch.Update += Extras.WrapILHook(IgnoreRepulsiveCreatureILHook);

        On.AbstractWorldEntity.Destroy += PreventCreatureDestructionHook;

        On.RainWorldGame.GameOver += InterruptGameOverHook;

        On.RoomRain.CreatureSmashedInGround += IgnorePlayerRainDeathHook;

        On.Watcher.WarpPoint.SpawnPendingObject += WarpDeathProtectionHook;

        manualHooks = new Hook[2];

        manualHooks[0] = new Hook(
            typeof(Creature).GetProperty(nameof(Creature.windAffectiveness)).GetGetMethod(),
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
        IL.BigEelAI.IUseARelationshipTracker_UpdateDynamicRelationship -= Extras.WrapILHook(IgnoreProtectedCreatureILHook);

        IL.BulletDrip.Strike -= Extras.WrapILHook(PreventRainDropStunILHook);
        IL.RoomRain.ThrowAroundObjects -= Extras.WrapILHook(PreventRoomRainPushILHook);

        IL.WormGrass.WormGrassPatch.Update -= Extras.WrapILHook(IgnoreRepulsiveCreatureILHook);

        On.AbstractWorldEntity.Destroy -= PreventCreatureDestructionHook;

        On.RainWorldGame.GameOver += InterruptGameOverHook;

        On.RoomRain.CreatureSmashedInGround -= IgnorePlayerRainDeathHook;

        On.Watcher.WarpPoint.SpawnPendingObject -= WarpDeathProtectionHook;

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
    private static bool AvoidImmunePlayerHook(Func<Creature, bool> orig, Creature self) =>
        DeathProtection.HasProtection(self) || orig.Invoke(self);

    /// <summary>
    /// Prevents the end of cycle rain from affecting Slugcat if protected.
    /// </summary>
    private static void IgnorePlayerRainDeathHook(On.RoomRain.orig_CreatureSmashedInGround orig, RoomRain self, Creature crit, float speed)
    {
        if (DeathProtection.HasProtection(crit)) return;

        orig.Invoke(self, crit, speed);
    }

    /// <summary>
    /// Prevents the player from being affected by wind while protected.
    /// </summary>
    private static float IgnoreWindAffectivenessHook(Func<Creature, float> orig, Creature self) =>
        DeathProtection.HasProtection(self)
            ? 0f
            : orig.Invoke(self);

    /// <summary>
    /// Prevents the game over screen from showing up if a player is currently being protected.
    /// </summary>
    private static void InterruptGameOverHook(On.RainWorldGame.orig_GameOver orig, RainWorldGame self, Creature.Grasp dependentOnGrasp)
    {
        if (self.Players.Any(ac => DeathProtection.HasProtection(ac.realizedCreature as Player))) return;

        orig.Invoke(self, dependentOnGrasp);
    }

    /// <summary>
    /// Prevents a creature's abstract representation from being destroyed while death-immune.
    /// </summary>
    private static void PreventCreatureDestructionHook(On.AbstractWorldEntity.orig_Destroy orig, AbstractWorldEntity self)
    {
        if (self is AbstractCreature abstractCreature
            && DeathProtection.HasProtection(abstractCreature.realizedCreature)) return;

        orig.Invoke(self);
    }

    private static bool WarpDeathProtectionHook(On.Watcher.WarpPoint.orig_SpawnPendingObject orig, Watcher.WarpPoint self, AbstractPhysicalObject nextObject, bool immediateSpawn)
    {
        if (orig.Invoke(self, nextObject, immediateSpawn))
        {
            if (nextObject is AbstractCreature abstractCreature
                && DeathProtection.TryGetProtection(abstractCreature.realizedCreature, out DeathProtection protection))
            {
                protection.RemoveFromRoom();

                self.room.AddObject(protection);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Causes Leviathan bites to ignore death-protected creatures, just like it now ignores the Safari mode Overseer.
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
         .Emit(OpCodes.Isinst, typeof(Creature))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (!(this.room.physicalObjects[j][k] is BigEel) && !DeathProtection.HasProtection(this.room.physicalObjects[j][k] as Creature) && ...) { ... }
    }

    /// <summary>
    ///     Causes Leviathans to ignore creatures under death protection.
    /// </summary>
    private static void IgnoreProtectedCreatureILHook(ILContext context)
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
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (this.eel.AmIHoldingCreature(dRelation.trackerRep.representedCreature) || DeathProtection.HasProtection(dRelation.trackerRep.representedCreature.realizedCreature) || dRelation.trackerRep.representedCreature.creatureTemplate.smallCreature) { ... }
    }

    /// <summary>
    /// Causes Worm Grass patches to fully ignore death-protected creatures.
    /// </summary>
    private static void IgnoreRepulsiveCreatureILHook(ILContext context)
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
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (realizedCreature != null && !DeathProtection.HasProtection(realizedCreature) && ...) { ... }
    }

    /// <summary>
    /// Prevents rain drops from stunning creatures while protected.
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
         .Emit(OpCodes.Isinst, typeof(Creature))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (collisionResult.chunk.owner is Creature && !DeathProtection.HasProtection(collisionResult.chunk.owner as Creature)) { ... }
    }

    /// <summary>
    /// Prevents the rain from pushing and stunning protected creatures.
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
         .Emit(OpCodes.Isinst, typeof(Creature))
         .EmitDelegate(DeathProtection.HasProtection);

        c.Emit(OpCodes.Brtrue, target);

        // Result: if (!DeathProtection.HasProtection(this.room.physicalObjects[i][j] as Creature) && (!ModManager.Watcher || !this.room.game.IsStorySession || this.room.physicalObjects[i][j].abstractPhysicalObject.rippleLayer == 0)) { ... }
    }
}