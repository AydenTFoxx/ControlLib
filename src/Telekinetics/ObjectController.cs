using System;
using ControlLib.Possession;
using ModLib.Collections;
using ModLib.Input;
using ModLib.Options;
using RWCustom;
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
    private static readonly WeakDictionary<PhysicalObject, ObjectController> _activeInstances = [];

    public PhysicalObject? Target
    {
        get;
        set
        {
            if (field is not null)
            {
                if (_activeInstances.ContainsKey(field))
                    _activeInstances.Remove(field);

                if (originalGravity != 0f)
                    field.SetLocalGravity(originalGravity);
            }

            field = value;

            if (value is not null)
            {
                originalGravity = value.GetLocalGravity();
                value.SetLocalGravity(0f);

                targetPos = new Vector2[value.bodyChunks.Length];

                _activeInstances[value] = this;
            }
        }
    }

    public Player? Owner
    {
        get;
        set => field = field is null ? value : throw new InvalidOperationException("Owner cannot be modified after being set to a non-null value.");
    }

    private Player.InputPackage input;
    private Player.InputPackage lastInput;

    public Player.InputPackage Input => input;

    private float originalGravity;
    private int life;

    private Creature.Grasp? TargetGrasp;

    private Vector2 targetRotation;
    private Vector2[]? targetPos;

    public override float VisibilityBonus => 0.1f + (Target is not null ? Target.VisibilityBonus : 0f);

    public ObjectController(AbstractPhysicalObject abstractController, PhysicalObject? target, Player? owner)
        : base(abstractController)
    {
        Target = target;
        Owner = owner;

        bodyChunks = [
            new BodyChunk(this, 0, Vector2.zero, 12f, 0f)
        ];
        bodyChunkConnections = [];
    }

    public void ThrowObject(int grasp)
    {
        if (Owner is null || Target is null) return;

        if (input.x == 0 && lastInput.x != 0)
            input.x = lastInput.x;

        if (input.y == 0 && lastInput.y != 0)
            input.y = lastInput.y;

        Owner.input[0] = input;

        if (input.x == 0 && input.y != 0)
        {
            Owner.animation = Player.AnimationIndex.Flip;
            Owner.jumpBoost = 3f;

            Owner.bodyChunks[0].vel.y += 4f;
        }

        Owner.grasps[grasp] = TargetGrasp;
        Owner.ThrowObject(grasp, evenUpdate);

        TargetGrasp = null;

        Destroy();
    }

    public override void Destroy()
    {
        base.Destroy();

        if (Target is not null && Owner is not null && Owner.TryGetPossessionManager(out PossessionManager manager))
        {
            manager.StopItemPossession(Target);
        }

        TargetGrasp?.Release();
        TargetGrasp = null;

        Target = null;

        room?.AddObject(new ReverseShockwave(firstChunk.pos, 16f, 0.125f, 15));
    }

    public override void Grabbed(Creature.Grasp grasp)
    {
        if (grasp.grabber is not Player player || (Owner is not null && player != Owner)) return;

        Owner ??= player;

        if (Target is not null)
        {
            TargetGrasp = new Creature.Grasp(Owner, Target, grasp.graspUsed, 0, Creature.Grasp.Shareability.CanOnlyShareWithNonExclusive, 0.5f, false);
            Target.Grabbed(TargetGrasp);
        }

        input.x = Owner.ThrowDirection;

        PickedUp(grasp.grabber);

        base.Grabbed(grasp);
    }

    public override void PickedUp(Creature upPicker)
    {
        if (Owner is not null && upPicker != Owner)
        {
            Destroy();
            return;
        }
        else
        {
            Owner ??= upPicker as Player;
        }

        if (Target is null || Owner is null) return;

        if (Target is PlayerCarryableItem carryableItem)
        {
            if (carryableItem is Spear spear)
            {
                spear.PulledOutOfStuckObject();
                spear.hasHorizontalBeamState = true;
                spear.PickedUp(upPicker);
                spear.ChangeMode(Weapon.Mode.Free);
            }
            else
            {
                carryableItem.PickedUp(upPicker);
            }
        }
        else
        {
            room.PlaySound(SoundID.Slugcat_Pick_Up_Misc_Inanimate, Target.firstChunk, false, 1f, 1f);
        }

        if (Target.graphicsModule != null && Owner.Grabability(Target) < (Player.ObjectGrabability)5)
        {
            Target.graphicsModule.BringSpritesToFront();
        }

        base.PickedUp(upPicker);
    }

    public override void PlaceInRoom(Room placeRoom)
    {
        base.PlaceInRoom(placeRoom);

        firstChunk.HardSetPosition(placeRoom.MiddleOfTile(abstractPhysicalObject.pos.Tile));
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (Target is null || Owner is null)
        {
            life++;
            if (life % 20 == 0)
            {
                life = 0;
                room.AddObject(new ShockWave(firstChunk.pos, 12f, 0.05f, 20, false));
            }
            return;
        }

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

        if (input.AnyInput)
            lastInput = input;

        input = Owner.GetRawInput();

        targetPos ??= new Vector2[Target.bodyChunks.Length];

        Vector2 moveDir = GetMoveDirection();
        for (int i = 0; i < Target.bodyChunks.Length; i++)
        {
            BodyChunk bodyChunk = Target.bodyChunks[i];

            if (moveDir != Vector2.zero)
                targetPos[i] = bodyChunk.pos + (moveDir * 10f);

            bodyChunk.pos = Vector2.SmoothDamp(bodyChunk.pos, targetPos[i], ref bodyChunk.vel, 2f, 6f);
        }

        if (input.thrw && TargetGrasp is not null)
        {
            ThrowObject(TargetGrasp.graspUsed);
            return;
        }
        else if (input.y < 0 && input.pckp)
        {
            Destroy();
            return;
        }

        if (Target is Weapon weapon)
        {
            int speedVel = OptionUtils.GetClientOptionValue(Options.WEAPON_ROTATION_SPEED);

            if (speedVel > 0)
            {
                weapon.rotationSpeed = speedVel;
            }
            else
            {
                targetRotation = Custom.DegToVec(Custom.AimFromOneVectorToAnother(Vector2.zero, new Vector2(lastInput.x, lastInput.y)));

                weapon.rotation = Vector2.Lerp(weapon.lastRotation, targetRotation, weapon.room.game.myTimeStacker);
            }
        }

        life++;

        int module = Owner.Adrenaline > 0f ? 4 : 8;
        if (life % module == 0)
        {
            life = 0;
            room.AddObject(new ShockWave(firstChunk.pos, 16f, 0.1f, module, false));
        }
    }

    // Adapted from Player.PointDir() method
    private Vector2 GetMoveDirection()
    {
        Vector2 analogueDir = input.analogueDir;

        return analogueDir.x != 0f || analogueDir.y != 0f
            ? analogueDir.normalized
            : input.ZeroGGamePadIntVec.x != 0 || input.ZeroGGamePadIntVec.y != 0
                ? input.IntVec.ToVector2().normalized
                : Vector2.zero;
    }

    public static bool HasController(PhysicalObject target) => _activeInstances.ContainsKey(target);

    public static bool TryGetController(PhysicalObject target, out ObjectController controller) => _activeInstances.TryGetValue(target, out controller);
}