using ControlLib.Telekinetics;
using ModLib;
using ModLib.Options;
using UnityEngine;

namespace ControlLib.Possession.Graphics;

public class TargetCursor(PossessionManager manager)
    : PlayerAccessory(manager)
{
    public Vector2 targetPos;
    public Vector2 lastTargetPos;

    private float targetAlpha;

    private FSprite? cursorSprite;

    private float CursorSpeed =>
        Extras.IsMultiplayer
        && !OptionUtils.IsOptionEnabled(Options.MULTIPLAYER_SLOWDOWN)
            ? 7.5f
            : 15f;

    private Color FlashColor => MindBlast.HasInstance(player)
        ? RainWorld.GoldRGB
        : Color.red;

    public Vector2 GetPos() => targetPos + camPos;

    public void ResetCursor(bool isVisible, bool forceAlpha = false)
    {
        targetPos = pos;
        lastTargetPos = targetPos;

        targetAlpha = isVisible ? 1f : 0f;

        if (forceAlpha)
        {
            alpha = targetAlpha;
            cursorSprite?.alpha = alpha;
        }

        if (isVisible)
        {
            cursorSprite?.SetPosition(targetPos);
        }
    }

    public void UpdateCursor(in Vector2 input)
    {
        lastTargetPos = targetPos;

        targetPos = RWCustomExts.ClampedDist(targetPos + (input * CursorSpeed), pos, TargetSelector.GetPossessionRange());
    }

    public override void TryRealizeInRoom(Room playerRoom)
    {
        base.TryRealizeInRoom(playerRoom);

        ResetCursor(false, forceAlpha: true);
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        lastTargetPos = targetPos;
    }

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        pos = GetMarkPos(camPos, timeStacker);

        if (targetAlpha != alpha)
        {
            alpha = Mathf.Clamp(alpha + ((targetAlpha - alpha) * 0.05f), 0f, 1f);
        }

        sLeaser.sprites[0].alpha = alpha;

        if (alpha <= 0f)
        {
            this.camPos = camPos;
            return;
        }

        UpdateColorLerp(Manager.TargetSelector is not null && Manager.TargetSelector.ExceededTimeLimit);

        sLeaser.sprites[0].color = Color.Lerp(Color.white, FlashColor, colorTime);

        sLeaser.sprites[0].SetPosition(Vector2.SmoothDamp(sLeaser.sprites[0].GetPosition(), targetPos, ref vel, 0.1f));

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[1];

        sLeaser.sprites[0] = cursorSprite = new FSprite("guardEye");

        base.InitiateSprites(sLeaser, rCam);
    }
}