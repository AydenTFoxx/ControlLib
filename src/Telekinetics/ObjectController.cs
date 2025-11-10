using System.Linq;
using ModLib.Input;
using UnityEngine;

namespace ControlLib.Telekinetics;

public class ObjectController : UpdatableAndDeletable
{
    private readonly PhysicalObject target;
    private readonly Player owner;

    private readonly float originalGravity;

    private Vector2 vel;

    public ObjectController(PhysicalObject target, Player owner)
    {
        this.target = target;
        this.owner = owner;

        int graspToUse = (owner.grasps[0] == null) ? 0 : 1;

        if (owner.grasps[graspToUse] is not null)
        {
            Main.Logger?.LogWarning("Tried grabbing an object with no available grasps; Destroying controller.");
            Destroy();
            return;
        }

        owner.SlugcatGrab(target, graspToUse);

        originalGravity = target.GetLocalGravity();
        target.SetLocalGravity(0f);
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (target is null or { slatedForDeletetion: true }
            || owner is null or { slatedForDeletetion: true }
            || (owner.room is not null && owner.room != room))
        {
            Destroy();
            return;
        }

        if (target.room is not null && target.room != room)
        {
            room?.RemoveObject(this);
            target.room.AddObject(this);
        }

        Player.InputPackage input = owner.GetRawInput();
        Vector2 moveDir = GetMoveDirection(input);

        if (moveDir != Vector2.zero)
        {
            vel = Vector2.ClampMagnitude(moveDir, 100f);
        }
        else
        {
            vel *= 0.04f;
        }

        if (vel != Vector2.zero)
        {
            target.WeightedPush(0, target.bodyChunks.Last().index, vel, 10f);
        }

        if (target is Weapon weapon)
        {
            weapon.rotationSpeed = 20f;
        }

        if (!owner.grasps.Any(g => g?.grabbed == target))
        {
            Destroy();
        }
    }

    public override void Destroy()
    {
        base.Destroy();

        target.SetLocalGravity(originalGravity);
    }

    private static Vector2 GetMoveDirection(Player.InputPackage input)
    {
        Vector2 analogueDir = input.analogueDir;

        return analogueDir.x != 0f || analogueDir.y != 0f
            ? analogueDir.normalized
            : input.ZeroGGamePadIntVec.x != 0 || input.ZeroGGamePadIntVec.y != 0
                ? input.IntVec.ToVector2().normalized
                : Vector2.zero;
    }
}