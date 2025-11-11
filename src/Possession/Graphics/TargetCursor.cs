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

    private float CursorSpeed =>
        Extras.IsMultiplayer
        && !OptionUtils.IsOptionEnabled(Options.MULTIPLAYER_SLOWDOWN)
            ? 7.5f
            : 15f;

    private Color FlashColor => MindBlast.HasInstance(player)
        ? RainWorld.GoldRGB
        : Color.red;

    public Vector2 GetPos() => targetPos + camPos;

    public void ResetCursor(bool isVisible = false)
    {
        targetPos = pos;
        lastTargetPos = targetPos;

        targetAlpha = isVisible ? 1f : 0f;
    }

    public void UpdateCursor(Player.InputPackage input)
    {
        lastTargetPos = targetPos;

        Vector2 goalPos = targetPos + (new Vector2(input.x, input.y) * CursorSpeed);
        float maxDist = TargetSelector.GetPossessionRange();

        targetPos = RWCustomExts.ClampedDist(goalPos, pos, maxDist);
    }

    public override void TryRealizeInRoom(Room playerRoom)
    {
        base.TryRealizeInRoom(playerRoom);

        ResetCursor();
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

        float smoothTime = timeStacker * timeStacker * (3f + (2f * timeStacker));

        sLeaser.sprites[0].SetPosition(Vector2.Lerp(lastTargetPos, targetPos, smoothTime));

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[1];

        sLeaser.sprites[0] = new FSprite("guardEye");

        base.InitiateSprites(sLeaser, rCam);
    }
}