using System;
using System.Linq;
using ControlLib.Possession;
using ModLib.Collections;
using ModLib.Input;
using ModLib.Options;
using RWCustom;
using UnityEngine;

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

                targetVel = new Vector2[value.bodyChunks.Length, 2];

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
    private Vector2[,]? targetVel;

    private float velMultiplier;

    private readonly bool spinClockwise = UnityEngine.Random.value <= 0.5f;

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

        if (this is { input.x: 0, lastInput.x: not 0 })
            input.x = lastInput.x;

        if (this is { input.y: 0, lastInput.y: not 0 })
            input.y = lastInput.y;

        Owner.input[0] = input;

        if (input is { x: 0, y: not 0 })
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

        if (this is { Target: not null, Owner: not null } && Owner.TryGetPossessionManager(out PossessionManager manager))
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

        Main.Logger.LogDebug($"ObjectController was picked up by {upPicker}!");

        if (Target is null || Owner is null) return;

        if (Target is PlayerCarryableItem carryableItem)
        {
            carryableItem.PickedUp(upPicker);
        }
        else
        {
            base.PickedUp(upPicker);
        }

        if (Target.graphicsModule != null && Owner.Grabability(Target) < (Player.ObjectGrabability)5)
        {
            Target.graphicsModule.BringSpritesToFront();
        }
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
            Target.RemoveFromRoom();
            RemoveFromRoom();

            Target.abstractPhysicalObject.Move(Owner.abstractCreature.pos);
            abstractPhysicalObject.Move(Owner.abstractCreature.pos);

            Target.PlaceInRoom(Owner.room);
            PlaceInRoom(Owner.room);
        }

        if (Target.room is not null && Target.room != room)
        {
            Target.RemoveFromRoom();

            Target.abstractPhysicalObject.Move(abstractPhysicalObject.pos);

            Target.PlaceInRoom(room);
        }

        if (input.AnyInput)
            lastInput = input;

        input = Owner.GetRawInput();

        if (targetVel is not null)
        {
            Vector2 moveDir = GetMoveDirection();
            for (int i = 0; i < Target.bodyChunks.Length; i++)
            {
                BodyChunk bodyChunk = Target.bodyChunks[i];

                targetVel[i, 1] = targetVel[i, 0];

                if (moveDir != Vector2.zero)
                {
                    targetVel[i, 0] = moveDir * (4 + velMultiplier);

                    velMultiplier = targetVel[i, 1].normalized == targetVel[i, 0].normalized
                        ? Mathf.Min(velMultiplier + 0.01f, 4f)
                        : Mathf.Max(velMultiplier - 0.001f, 0f);
                }
                else
                {
                    targetVel[i, 0] -= targetVel[i, 0] * 0.05f;

                    velMultiplier = Mathf.Max(velMultiplier - 0.005f, 0f);
                }

                bodyChunk.vel = Vector2.MoveTowards(bodyChunk.vel, targetVel[i, 0], 240f);
            }
        }

        if (this is { input.thrw: true, TargetGrasp: not null })
        {
            ThrowObject(TargetGrasp.graspUsed);
            return;
        }
        else if (input is { y: < 0, pckp: true })
        {
            Destroy();
            return;
        }

        if (Target is Weapon weapon)
        {
            int speedVel = OptionUtils.GetClientOptionValue(Options.WEAPON_ROTATION_SPEED);

            if (speedVel > 0)
            {
                weapon.rotationSpeed = spinClockwise ? speedVel : -speedVel;
            }
            else
            {
                Vector2 targetRotation = Custom.DegToVec(Custom.AimFromOneVectorToAnother(Vector2.zero, new Vector2(lastInput.x, lastInput.y)));

                weapon.rotation = Vector2.Lerp(weapon.lastRotation, targetRotation, weapon.lastRotation.magnitude / targetRotation.magnitude);
            }

            if (weapon.room is not null)
            {
                foreach (ScavengerAI scavAI in weapon.room.updateList.OfType<Scavenger>().Select(scav => scav.AI))
                {
                    if (scavAI.idleCounter > 0)
                        scavAI.MakeLookHere(weapon.firstChunk.pos);
                }
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

    public override string ToString() => $"{nameof(ObjectController)}: Target: {Target} | Owner: {Owner}";

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