using System.Linq;
using Possessions.Possession;
using ModLib.Collections;
using ModLib.Input;
using ModLib.Options;
using RWCustom;
using UnityEngine;

namespace Possessions.Telekinetics;

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

                _activeInstances[value] = this;
            }
        }
    }

    public Player? Owner { get; set; }

    private Player.InputPackage input;
    private Player.InputPackage lastInput;

    public Player.InputPackage Input => input;

    private float originalGravity;
    private int life;

    private Creature.Grasp? TargetGrasp;
    private Vector2 targetVel;

    private Vector2 vel;
    private float velMultiplier;

    private readonly bool spinClockwise = Random.value <= 0.5f;

    public ObjectController(AbstractPhysicalObject abstractController, PhysicalObject? target, Player? owner)
        : base(abstractController)
    {
        Target = target;
        Owner = owner;

        bodyChunks = [
            new BodyChunk(this, 0, Vector2.zero, 12f, 0.01f)
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
        Owner = null;

        room?.AddObject(new ReverseShockwave(firstChunk.pos, 16f, 0.125f, 15));
    }

    public override void Grabbed(Creature.Grasp grasp)
    {
        if (grasp.grabber is not Player player || (Owner is not null && player != Owner)) return;

        PickedUp(grasp.grabber);

        Owner ??= player;

        if (Target is not null)
        {
            TargetGrasp = new Creature.Grasp(Owner, Target, grasp.graspUsed, 0, Creature.Grasp.Shareability.CanOnlyShareWithNonExclusive, 0.5f, false);
            Target.Grabbed(TargetGrasp);
        }

        input.x = Owner.ThrowDirection;

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

        Main.Logger.LogDebug($"[{this}] was picked up by {upPicker}!");

        if (Target is null || Owner is null) return;

        if (Target is IHaveAStalk haveAStalk)
        {
            haveAStalk.DetatchStalk();
        }

        if (Target is PlayerCarryableItem carryableItem)
        {
            if (Target is Spear { stuckInWall: not null, room.readyForAI: true } spear)
            {
                spear.resetHorizontalBeamState();
            }

            carryableItem.PickedUp(upPicker);
        }
        else
        {
            base.PickedUp(upPicker);
        }

        Target.graphicsModule?.BringSpritesToFront();
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

        Vector2 moveDir = GetMoveDirection();
        for (int i = 0; i < Target.bodyChunks.Length; i++)
        {
            BodyChunk bodyChunk = Target.bodyChunks[i];

            if (moveDir != Vector2.zero)
            {
                velMultiplier = Mathf.Min(velMultiplier + 0.05f, 16f);

                targetVel = moveDir * (4f + velMultiplier);
            }
            else if (targetVel != Vector2.zero)
            {
                targetVel = Vector2.Max(targetVel - (targetVel * 0.05f), Vector2.zero);

                velMultiplier = targetVel == Vector2.zero ? 0f : Mathf.Max(velMultiplier - 0.1f, 0f);
            }

            bodyChunk.vel = Vector2.SmoothDamp(bodyChunk.vel, targetVel, ref vel, 0.1f);
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
                foreach (ScavengerAI scavAI in weapon.room.updateList.OfType<Scavenger>().Where(static scav => scav.Consious).Select(static scav => scav.AI))
                {
                    if (!scavAI.VisualContact(weapon.firstChunk)) continue;

                    if (scavAI.itemTracker is not null && scavAI.itemTracker.items.Contains(scavAI.itemTracker.RepresentationForObject(Target, false)))
                    {
                        scavAI.itemTracker.SeeItem(Target.abstractPhysicalObject);

                        scavAI.MakeLookHere(weapon.firstChunk.pos);
                    }
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

    public override string ToString() => $"{nameof(ObjectController)} (Target: {Target}; Owner: {Owner})";

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