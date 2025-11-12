using ControlLib.Enums;
using RWCustom;
using UnityEngine;

namespace ControlLib.Telekinetics;

public static class TelekineticsHooks
{
    public static void ApplyHooks()
    {
        On.AbstractPhysicalObject.Realize += RealizeControllerHook;

        On.Player.Grabability += ObjectControllerGrababilityHook;

        On.Weapon.Thrown += ThrownWeaponFromControllerHook;
    }

    public static void RemoveHooks()
    {
        On.AbstractPhysicalObject.Realize -= RealizeControllerHook;

        On.Player.Grabability -= ObjectControllerGrababilityHook;

        On.Weapon.Thrown -= ThrownWeaponFromControllerHook;
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