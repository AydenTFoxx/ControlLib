using System;
using RWCustom;
using UnityEngine;

namespace ControlLib.Possession.Graphics;

public abstract class PlayerAccessory : CosmeticSprite
{
    protected PossessionManager Manager { get; }

    public Vector2 camPos;
    public float alpha;

    protected float colorTime;
    protected bool invertColorLerp;

    protected readonly Player player;

    public PlayerAccessory(PossessionManager manager)
    {
        Manager = manager;
        player = manager.GetPlayer();

        player.room?.AddObject(this);
    }

    public virtual void TryRealizeInRoom(Room playerRoom)
    {
        room?.RemoveObject(this);

        playerRoom.AddObject(this);

        pos = player.mainBodyChunk.pos - camPos;
    }

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

        if (player.room is not null && player.room != room)
        {
            TryRealizeInRoom(player.room);
        }
    }

    public override void AddToContainer(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, FContainer newContatiner)
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

        AddToContainer(sLeaser, rCam, null!);
    }

    protected static float ClampedDist(float targetPos, float refPos, float maxDist) =>
        Mathf.Clamp(targetPos, refPos - maxDist, refPos + maxDist);

    protected static Vector2 ClampedDist(Vector2 targetPos, Vector2 refPos, float maxDist) =>
        new(ClampedDist(targetPos.x, refPos.x, maxDist), ClampedDist(targetPos.y, refPos.y, maxDist));

    protected static Vector2 GetMarkPos(Player player, Vector2 camPos, float timeStacker)
    {
        if (player?.graphicsModule is not PlayerGraphics playerGraphics) return default;

        Vector2 vector2 = Vector2.Lerp(playerGraphics.drawPositions[1, 1], playerGraphics.drawPositions[1, 0], timeStacker);
        Vector2 vector3 = Vector2.Lerp(playerGraphics.head.lastPos, playerGraphics.head.pos, timeStacker);

        return vector3 + Custom.DirVec(vector2, vector3) + new Vector2(0f, 30f) - camPos;
    }
}