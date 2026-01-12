using Possessions.Telekinetics;
using ModLib;
using ModLib.Options;
using UnityEngine;

namespace Possessions.Possession.Graphics;

/// <summary>
///     A controllable sprite used for selecting creatures for possession, akin to to Saint's karmic burst sprite.
/// </summary>
/// <param name="manager">The <see cref="PossessionManager"/> this accessory is tied to.</param>
public class TargetCursor(PossessionManager manager) : PlayerAccessory(manager)
{
    private Vector2 targetPos;
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

    /// <summary>
    ///     Retrieves the position targeted by the cursor, accounting for the camera's position.
    /// </summary>
    /// <returns>The position of the cursor plus the camera's position.</returns>
    public Vector2 GetPos() => targetPos + camPos;

    /// <summary>
    ///     Retrieves the raw position targeted by the cursor, without accounting for the camera's position.
    /// </summary>
    /// <returns>The position of the cursor.</returns>
    public Vector2 GetRawPos() => targetPos;

    public void ResetCursor(bool isVisible, bool forceAlpha = false)
    {
        targetPos = pos;

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

    public void UpdateCursor(in Vector2 input) =>
        targetPos = RWCustomExts.ClampedDist(targetPos + (input * CursorSpeed), pos, TargetSelector.GetPossessionRange());

    public override void TryRealizeInRoom(Room newRoom, Vector2 newPos)
    {
        base.TryRealizeInRoom(newRoom, newPos);

        ResetCursor(false, forceAlpha: true);
    }

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        pos = GetMarkPos(camPos, timeStacker);

        UpdateAlpha(targetAlpha, maxDelta: 0.025f);

        if (alpha <= 0f)
        {
            if (justReachedTargetAlpha)
                cursorSprite?.alpha = 0f;
            return;
        }

        UpdateColorLerp(Manager is { TargetSelector: not null, TargetSelector.ExceededTimeLimit: true });

        cursorSprite ??= sLeaser.sprites[0];

        cursorSprite.alpha = alpha;
        cursorSprite.color = Color.Lerp(Color.white, FlashColor, colorTime);

        cursorSprite.SetPosition(Vector2.SmoothDamp(cursorSprite.GetPosition(), targetPos, ref velocity, 0.1f));

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[1];

        sLeaser.sprites[0] = cursorSprite = new FSprite("guardEye") { alpha = 0f };

        base.InitiateSprites(sLeaser, rCam);
    }
}