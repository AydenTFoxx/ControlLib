using System.Runtime.CompilerServices;
using UnityEngine;

namespace ControlLib;

public static class RWCustomExts
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ClampedDist(float targetPos, float refPos, float maxDist) =>
        Mathf.Clamp(targetPos, refPos - maxDist, refPos + maxDist);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ClampedDist(Vector2 targetPos, Vector2 refPos, float maxDist) =>
        new(ClampedDist(targetPos.x, refPos.x, maxDist), ClampedDist(targetPos.y, refPos.y, maxDist));


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StunAllGrasps(this PhysicalObject self, int stun)
    {
        for (int i = self.grabbedBy.Count - 1; i >= 0; i--)
        {
            Creature? grabber = self.grabbedBy[i]?.grabber;

            if (grabber is null) return;

            grabber.LoseAllGrasps();
            grabber.Stun(stun);
        }
    }
}