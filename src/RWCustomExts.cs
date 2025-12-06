using System.Linq;
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
    public static Vector2 SmoothStep(Vector2 start, Vector2 end, float t) =>
        new(Mathf.SmoothStep(start.x, end.x, t), Mathf.SmoothStep(start.y, end.y, t));


    public static void StunAllGrasps(this PhysicalObject self, int stun)
    {
        int maxAttempts = 10;
        while (self.grabbedBy.Count > 0 && maxAttempts > 0)
        {
            Creature? grabber = self.grabbedBy.FirstOrDefault()?.grabber;

            if (grabber is null) break;

            grabber.LoseAllGrasps();
            grabber.Stun(stun);

            maxAttempts--;
        }
    }
}