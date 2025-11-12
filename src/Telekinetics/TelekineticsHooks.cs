using System;
using ControlLib.Enums;
using MonoMod.RuntimeDetour;
using RWCustom;
using UnityEngine;

namespace ControlLib.Telekinetics;

public static class TelekineticsHooks
{
    private static readonly Hook[] manualHooks;
    private static readonly HookConfig Config;

    static TelekineticsHooks()
    {
        Config = (typeof(DetourContext).GetField("Current")?.GetValue(null) as DetourContext)?.HookConfig ?? new();
        Config.ManualApply = true;

        manualHooks = new Hook[1];
        manualHooks[0] = new Hook(
            typeof(Player).GetProperty(nameof(Player.ThrowDirection)).GetGetMethod(),
            PossessedThrowDirectionHook,
            Config);
    }

    public static void ApplyHooks()
    {
        On.AbstractPhysicalObject.Realize += RealizeControllerHook;

        On.Player.Grabability += ObjectControllerGrababilityHook;

        On.Weapon.Thrown += ThrownWeaponFromControllerHook;

        foreach (Hook hook in manualHooks)
        {
            hook.Apply();
        }
    }

    public static void RemoveHooks()
    {
        On.AbstractPhysicalObject.Realize -= RealizeControllerHook;

        On.Player.Grabability -= ObjectControllerGrababilityHook;

        On.Weapon.Thrown -= ThrownWeaponFromControllerHook;

        foreach (Hook hook in manualHooks)
        {
            hook.Undo();
        }
    }

    private static Player.ObjectGrabability ObjectControllerGrababilityHook(On.Player.orig_Grabability orig, Player self, PhysicalObject obj) =>
        obj is ObjectController
            ? Player.ObjectGrabability.BigOneHand
            : orig.Invoke(self, obj);

    private static int PossessedThrowDirectionHook(Func<Player, int> orig, Player self) =>
        ObjectController.TryGetController(self, out ObjectController controller)
            ? controller.Input.x
            : orig.Invoke(self);

    private static void RealizeControllerHook(On.AbstractPhysicalObject.orig_Realize orig, AbstractPhysicalObject self)
    {
        orig.Invoke(self);

        if (self.type == AbstractObjectTypes.ObjectController)
            self.realizedObject ??= new ObjectController(self, null, null);
    }

    private static void ThrownWeaponFromControllerHook(On.Weapon.orig_Thrown orig, Weapon self, Creature thrownBy, Vector2 thrownPos, Vector2? firstFrameTraceFromPos, IntVector2 throwDir, float frc, bool eu)
    {
        if (ObjectController.TryGetController(self, out _))
        {
            Vector2 vector = self.firstChunk.pos + (throwDir.ToVector2() * 10f) + new Vector2(0f, 4f);
            if (self.room.GetTile(vector).Solid)
            {
                vector = self.firstChunk.pos;
            }
            thrownPos = vector;
            firstFrameTraceFromPos = self.firstChunk.pos - (throwDir.ToVector2() * 10f);

            Main.Logger?.LogDebug($"Throwing weapon from {thrownPos} (thrower is at: {thrownBy.mainBodyChunk.pos})");
        }
        orig.Invoke(self, thrownBy, thrownPos, firstFrameTraceFromPos, throwDir, frc, eu);
    }
}