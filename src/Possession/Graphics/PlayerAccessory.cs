using System;
using RWCustom;
using UnityEngine;

namespace ControlLib.Possession.Graphics;

public abstract class PlayerAccessory : CosmeticSprite
{
    protected PossessionManager Manager { get; }
    protected readonly Player player;

    public Vector2 camPos;
    public float alpha;
    public float targetAlpha;

    protected Vector2 velocity;

    protected float colorTime;
    protected bool invertColorLerp;

    private Vector2 lastMarkPos;

    protected bool FollowsPlayer { get; }

    public PlayerAccessory(PossessionManager manager, bool followsPlayer = true)
    {
        FollowsPlayer = followsPlayer;

        Manager = manager;
        player = manager.GetPlayer();

        player.room?.AddObject(this);
    }

    public PlayerAccessory(Player owner, bool followsPlayer = true)
    {
        FollowsPlayer = followsPlayer;

        Manager = null!;
        player = owner;

        owner.room?.AddObject(this);
    }

    public virtual void TryRealizeInRoom(Room newRoom, Vector2 newPos)
    {
        room?.RemoveObject(this);

        newRoom.AddObject(this);

        pos = newPos;
    }

    public float UpdateAlpha(float speed = 0.05f) =>
        targetAlpha == alpha
            ? alpha
            : (alpha = Mathf.Clamp01(alpha + ((targetAlpha - alpha) * speed)));

    public void UpdateColorLerp(bool applyLerp)
    {
        if (applyLerp)
        {
            colorTime += invertColorLerp ? 0.1f : -0.1f;

            if (Math.Abs(colorTime) >= 1f)
                invertColorLerp = !invertColorLerp;
        }
        else if (colorTime > 0f)
        {
            colorTime = Math.Max(0f, colorTime - 0.1f);
        }
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (player is null)
        {
            Destroy();
            return;
        }

        if (this is { FollowsPlayer: true, player.room: not null } && player.room != room)
        {
            TryRealizeInRoom(player.room, player.mainBodyChunk.pos - camPos);
        }
    }

    public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer? newContatiner)
    {
        newContatiner ??= rCam.ReturnFContainer("HUD");

        foreach (FSprite sprite in sLeaser.sprites)
        {
            sprite.RemoveFromContainer();
            newContatiner.AddChild(sprite);
        }

        if (sLeaser.containers != null)
        {
            foreach (FContainer node2 in sLeaser.containers)
            {
                newContatiner.AddChild(node2);
            }
        }
    }

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        this.camPos = camPos;

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        camPos = rCam.pos;

        AddToContainer(sLeaser, rCam, null);
    }

    protected Vector2 GetMarkPos(Vector2 camPos, float timeStacker)
    {
        if (player?.graphicsModule is PlayerGraphics playerGraphics)
        {
            Vector2 vector2 = Vector2.Lerp(playerGraphics.drawPositions[1, 1], playerGraphics.drawPositions[1, 0], timeStacker);
            Vector2 vector3 = Vector2.Lerp(playerGraphics.head.lastPos, playerGraphics.head.pos, timeStacker);

            lastMarkPos = vector3 + Custom.DirVec(vector2, vector3) + new Vector2(0f, 30f) - camPos;
        }
        return lastMarkPos;
    }
}