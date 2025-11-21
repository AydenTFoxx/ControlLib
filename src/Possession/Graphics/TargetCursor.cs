using ControlLib.Telekinetics;
using ModLib;
using ModLib.Options;
using UnityEngine;

namespace ControlLib.Possession.Graphics;

public class TargetCursor(PossessionManager manager) : PlayerAccessory(manager)
{
    public Vector2 targetPos;
    public Vector2 lastTargetPos;

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

    public override void TryRealizeInRoom(Room newRoom, Vector2 newPos)
    {
        base.TryRealizeInRoom(newRoom, newPos);

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

        cursorSprite ??= sLeaser.sprites[0];

        cursorSprite.alpha = UpdateAlpha();

        if (alpha <= 0f)
        {
            this.camPos = camPos;
            return;
        }

        UpdateColorLerp(Manager is { TargetSelector: not null, TargetSelector.ExceededTimeLimit: true });

        cursorSprite.color = Color.Lerp(Color.white, FlashColor, colorTime);

        cursorSprite.SetPosition(Vector2.SmoothDamp(cursorSprite.GetPosition(), targetPos, ref velocity, 0.1f));

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[1];

        sLeaser.sprites[0] = cursorSprite = new FSprite("guardEye");

        base.InitiateSprites(sLeaser, rCam);
    }
}