using System;
using System.Linq;
using ModLib.Input;
using UnityEngine;

/**
- Player "possesses" object, and can move it freely in air.
  Slugcat's eyes remain open, and follow the object around (but can still look at predators if they're closer)

- Object receives player input, and flies akin to Saint's Attunement ability.
  When receiving a throw input, the object is thrown as if Slugcat had thrown it, with the same behaviors and limitations.

- The object can be dropped with the same inputs for dropping items (down + pickup)

- If the object is thrown or dropped, or Slugcat is unconscious, the possession ends.

- OPTIONAL: Scavengers within line of sight of the player and the held object will have their `tempLike` influenced
  by their current opinion of Slugcat; Friendly scavengers will gain a greater respect, while afraid or aggressive scavs will be even more aggressive
-- Probably also use ShockReaction(), and influence `fear` (reduced by `know`), if possible.
*/

namespace ControlLib.Telekinetics;

public class ObjectController : PlayerCarryableItem
{
    public PhysicalObject? Target
    {
        get;
        set
        {
            if (field is not null && originalGravity != 0f)
                field.SetLocalGravity(originalGravity);

            field = value;

            if (value is not null)
            {
                originalGravity = value.GetLocalGravity();
                value.SetLocalGravity(0f);
            }
        }
    }

    public Player? Owner
    {
        get;
        set => field = field is null ? value : throw new InvalidOperationException("Owner cannot be modified after being set to a non-null value.");
    }

    private float originalGravity;
    private Vector2 vel;

    private int life;

    private Weapon? Weapon => Target as Weapon;

    public override float VisibilityBonus => 0.1f + (Target is not null ? Target.VisibilityBonus : 0f);

    public ObjectController(AbstractPhysicalObject abstractController, PhysicalObject? target, Player? owner)
        : base(abstractController)
    {
        Target = target;
        Owner = owner;
    }

    public void ThrowObject(int grasp)
    {
        if (Owner is null || Target is null) return;

        Owner.grasps[grasp] = Target.grabbedBy.First(g => g?.grabber == Owner);
        Owner.ThrowObject(grasp, evenUpdate);

        Destroy();
    }

    public override void Destroy()
    {
        base.Destroy();

        Target = null;
    }

    public override void Grabbed(Creature.Grasp grasp)
    {
        if (Owner is not null && grasp.grabber != Owner) return;

        Target?.Grabbed(grasp);
        base.Grabbed(grasp);
    }

    public override void PickedUp(Creature upPicker)
    {
        if (Owner is not null && upPicker != Owner)
            Destroy();
        else
            base.PickedUp(upPicker);
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (room is null || Target is null || Owner is null) return;

        if (grabbedBy.Count == 0)
        {
            Destroy();
            return;
        }

        if (Owner.room is not null && Owner.room != room)
        {
            room.RemoveObject(this);
            room.RemoveObject(Target);

            Owner.room.AddObject(this);
            Owner.room.AddObject(Target);
        }

        if (Target.room is not null && Target.room != room)
        {
            Target.room.RemoveObject(Target);

            room.AddObject(Target);
        }

        Player.InputPackage input = Owner.GetRawInput();
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
            Target.WeightedPush(0, Target.bodyChunks.Last().index, vel, 10f);
        }

        Weapon?.rotationSpeed = 20f;

        life++;

        int module = Owner.Adrenaline > 0f ? 4 : 8;
        if (life % module == 0)
        {
            life = 0;
            room.AddObject(new ShockWave(firstChunk.pos, 16f, 0.1f, module, false));
        }
    }

    // Adapted from Player.PointDir() method
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