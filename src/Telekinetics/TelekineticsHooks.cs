using System.Reflection;
using ControlLib.Enums;
using ModLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RWCustom;
using UnityEngine;

namespace ControlLib.Telekinetics;

public static class TelekineticsHooks
{
    public static void ApplyHooks()
    {
        Extras.WrapAction(static () =>
        {
            IL.Creature.RippleViolenceCheck += NoViolenceWhileProtectedILHook;

            IL.LocustSystem.Update += DelayLocustSpawningILHook;

            IL.Player.TossObject += TossPossessedItemILHook;
        });

        On.AbstractPhysicalObject.Realize += RealizeControllerHook;

        On.LocustSystem.WarmUp += DelayLocustWarmUpHook;

        On.Player.Grabability += ObjectControllerGrababilityHook;

        On.Weapon.Thrown += ThrownWeaponFromControllerHook;
    }

    public static void RemoveHooks()
    {
        Extras.WrapAction(static () =>
        {
            IL.Creature.RippleViolenceCheck -= NoViolenceWhileProtectedILHook;

            IL.LocustSystem.Update -= DelayLocustSpawningILHook;

            IL.Player.TossObject -= TossPossessedItemILHook;
        });

        On.AbstractPhysicalObject.Realize -= RealizeControllerHook;

        On.LocustSystem.WarmUp -= DelayLocustWarmUpHook;

        On.Player.Grabability -= ObjectControllerGrababilityHook;

        On.Weapon.Thrown -= ThrownWeaponFromControllerHook;
    }

    /// <summary>
    /// Prevents locust systems from spawning cloud locusts while their spawnMultiplier field is negative.
    /// </summary>
    private static void DelayLocustWarmUpHook(On.LocustSystem.orig_WarmUp orig, LocustSystem self)
    {
        if (self.spawnMultiplier <= 0f) return;

        orig.Invoke(self);
    }

    private static Player.ObjectGrabability ObjectControllerGrababilityHook(On.Player.orig_Grabability orig, Player self, PhysicalObject obj) =>
        obj is ObjectController
            ? Player.ObjectGrabability.BigOneHand
            : orig.Invoke(self, obj);

    private static void RealizeControllerHook(On.AbstractPhysicalObject.orig_Realize orig, AbstractPhysicalObject self)
    {
        orig.Invoke(self);

        if (self.type == AbstractObjectTypes.ObjectController)
            self.realizedObject ??= new ObjectController(self, null, null);
    }

    private static void ThrownWeaponFromControllerHook(On.Weapon.orig_Thrown orig, Weapon self, Creature thrownBy, Vector2 thrownPos, Vector2? firstFrameTraceFromPos, IntVector2 throwDir, float frc, bool eu)
    {
        if (ObjectController.TryGetController(self, out ObjectController controller))
        {
            if (controller.Input.IntVec != new IntVector2(0, 0))
                throwDir = controller.Input.IntVec;

            Vector2 vector = self.firstChunk.pos + (throwDir.ToVector2() * 10f) + new Vector2(0f, 4f);
            if (self.room.GetTile(vector).Solid)
            {
                vector = self.firstChunk.pos;
            }
            thrownPos = vector;

            firstFrameTraceFromPos = self.firstChunk.pos - (throwDir.ToVector2() * 10f);

            Main.Logger.LogDebug($"Throwing weapon from {thrownPos}; Direction: {throwDir} (first frame trace: {firstFrameTraceFromPos})");
        }

        orig.Invoke(self, thrownBy, thrownPos, firstFrameTraceFromPos, throwDir, frc, eu);

        if (controller is not null)
        {
            self.changeDirCounter = 0;
        }
    }

    /// <summary>
    /// Prevents cloud locusts from spawning while their system's spawnMultiplier field is below zero.
    /// </summary>
    private static void DelayLocustSpawningILHook(ILContext context)
    {
        ILCursor c = new(context);

        c.GotoNext(static x => x.MatchStloc(1))
         .GotoPrev(MoveType.After, static x => x.MatchPop())
         .MoveAfterLabels();

        // Target: float num2 = 1f / this.LocustsPerTick;
        //        ^ HERE (Prepend)

        ILCursor d = new(c);

        d.GotoNext(MoveType.Before,
            static x => x.MatchLdarg(0),
            static x => x.MatchCall<LocustSystem>(nameof(LocustSystem.DoGrounding))
        );

        ILLabel target = d.MarkLabel();

        // Target: this.DoGrounding();
        //        ^ HERE (Referenced for redirect)

        c.Emit(OpCodes.Ldarg_0)
         .Emit(OpCodes.Ldfld, typeof(LocustSystem).GetField(nameof(LocustSystem.spawnMultiplier), BindingFlags.NonPublic | BindingFlags.Instance))
         .Emit(OpCodes.Ldc_R4, 0f)
         .Emit(OpCodes.Ble_Un, target);

        // Result:
        //
        // if (this.spawnMultiplier > 0)
        // {
        //     float num2 = 1f / this.LocustsPerTick;
        //     this.spawnCounter += 1f;
        //     while (this.spawnCounter > num2)
        //     {
        //         this.spawnCounter -= num2;
        //         this.AddCloudLocust(this.minX + this.spawnCounter * 15.999996f);
        //     }
        // }
        // this.DoGrounding();
    }

    /// <summary>
    ///     Prevents any violence against creatures who are death-protected. Why is it an IL hook? Because silly.
    /// </summary>
    private static void NoViolenceWhileProtectedILHook(ILContext context)
    {
        ILCursor c = new(context);
        ILCursor d = new(context);

        ILLabel? target = null;

        c.GotoNext(x => x.MatchBrfalse(out target));

        // Target: if (source != null && source.owner.abstractPhysicalObject.rippleLayer != this.abstractCreature.rippleLayer && !source.owner.abstractPhysicalObject.rippleBothSides && !this.abstractCreature.rippleBothSides) { ... }
        //                           ^ HERE (Insert)

        d.Emit(OpCodes.Ldarg_1).EmitDelegate(DeathProtection.HasProtection);
        d.Emit(OpCodes.Brtrue, target);

        // Result: if (source != null && !DeathProtection.HasProtection(source.owner as Creature) && source.owner.abstractPhysicalObject.rippleLayer != this.abstractCreature.rippleLayer && !source.owner.abstractPhysicalObject.rippleBothSides && !this.abstractCreature.rippleBothSides) { ... }
    }

    private static void TossPossessedItemILHook(ILContext context)
    {
        ILCursor c = new(context);

        c.GotoNext(static x => x.MatchStloc(4)).MoveAfterLabels();

        // Target: float num3 = ((this.ThrowDirection < 0) ? Mathf.Min(base.bodyChunks[0].pos.x, base.bodyChunks[1].pos.x) : Mathf.Max(base.bodyChunks[0].pos.x, base.bodyChunks[1].pos.x));
        //                      ^ HERE (Override)

        c.Emit(OpCodes.Ldloc_0).EmitDelegate(OverrideTossPosition);

        // Result: float num3 = OverrideTossPosition((this.ThrowDirection < 0) ? Mathf.Min(base.bodyChunks[0].pos.x, base.bodyChunks[1].pos.x) : Mathf.Max(base.bodyChunks[0].pos.x, base.bodyChunks[1].pos.x), grabbed);

        ILLabel? target = null;

        c.GotoNext(static x => x.MatchCall(typeof(Player).GetMethod(nameof(Player.HeavyCarry))))
         .GotoNext(MoveType.After, x => x.MatchBgeUn(out target))
         .MoveAfterLabels();

        // Target: if (!this.HeavyCarry(grabbed) && grabbed.TotalMass < base.TotalMass * 0.75f) { ... }
        //                                                                                    ^ HERE (Append)

        c.Emit(OpCodes.Ldloc_0).EmitDelegate(ObjectController.HasController);
        c.Emit(OpCodes.Brtrue, target);

        // Result: if (!this.HeavyCarry(grabbed) && grabbed.TotalMass < base.TotalMass * 0.75f && !ObjectController.HasController(grabbed)) { ... }

        static float OverrideTossPosition(float value, PhysicalObject grabbed)
        {
            return ObjectController.TryGetController(grabbed, out ObjectController controller)
                ? controller.Input.x < 0
                    ? Mathf.Min(grabbed.bodyChunks[0].pos.x, grabbed.bodyChunks[grabbed.bodyChunks.Length - 1].pos.x)
                    : Mathf.Max(grabbed.bodyChunks[0].pos.x, grabbed.bodyChunks[grabbed.bodyChunks.Length - 1].pos.x)
                : value;
        }
    }
}