using RWCustom;
using UnityEngine;

namespace Possessions.Possession.Graphics;

/// <summary>
///     A visual display of the player's <c>PossessionTime</c> value, similar to Saint's ascension time displayed when using or recharging their ability.
/// </summary>
/// <param name="manager">The <see cref="PossessionManager"/> this accessory is tied to.</param>
public class PossessionTimer(PossessionManager manager) : PlayerAccessory(manager)
{
    /// <summary>
    ///     If set to a non-null value, forces this accessory to follow a <see cref="PossessionMark"/> object instead of the player.
    /// </summary>
    public PossessionMark? FollowMark
    {
        get;
        set
        {
            field = value;
            FollowCreature = value?.Target;
        }
    }

    private readonly Color PipColor = Color.Lerp(PlayerGraphics.SlugcatColor((manager.GetPlayer().graphicsModule as PlayerGraphics)?.CharacterForColor), Color.white, 0.5f);
    private readonly int PipSpritesLength = Mathf.Clamp(manager.MaxPossessionTime / 30, 1, 32);

    private float rubberRadius;

    private bool ShouldShowPips => player is not null and { dead: false, inShortcut: false } && (Manager.IsPossessing || ((!Manager.IsSofanthielSlugcat || player.FoodInStomach >= player.MaxFoodInStomach) && Manager.PossessionTime < Manager.MaxPossessionTime) || Manager.ForceVisiblePips != 0);

    private Color FlashingPipColor =>
        Manager.TargetSelector?.State is TargetSelector.QueryingState
            ? PipColor == Color.white
                ? RainWorld.GoldRGB
                : Color.white
            : Color.red;

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        if (FollowMark is not null and { slatedForDeletetion: true })
            FollowMark = null;

        lastPos = pos;
        pos = FollowMark is not null
            ? FollowMark.MarkPos
            : alpha > 0f
                ? Vector2.SmoothDamp(lastPos, GetMarkPos(camPos, timeStacker), ref velocity, 0.05f)
                : GetMarkPos(camPos, timeStacker);

        UpdateAlpha(ShouldShowPips, maxDelta: 0.015f);

        if (alpha <= 0f)
        {
            if (justReachedTargetAlpha)
            {
                for (int m = 0; m < sLeaser.sprites.Length; m++)
                {
                    sLeaser.sprites[m].alpha = 0f;
                }
            }
            return;
        }

        UpdateColorLerp(Manager.LowPossessionTime || Manager.TargetSelector?.State is TargetSelector.QueryingState);

        float pipScale = Manager.MaxPossessionTime / PipSpritesLength;

        float radius = Manager.IsPossessing ? 12f : 6f;
        rubberRadius += (radius - rubberRadius) * 0.045f;

        if (rubberRadius < 5f)
        {
            rubberRadius = radius;
        }

        for (int i = 0; i < sLeaser.sprites.Length; i++)
        {
            FSprite pip = sLeaser.sprites[i];

            pip.scale = Manager.PossessionTime <= (pipScale * i)
                ? 0f
                : Manager.PossessionTime >= (pipScale * (i + 1))
                    ? 1f
                    : (Manager.PossessionTime - (pipScale * i)) / pipScale;

            pip.alpha = alpha;
            pip.color = Color.Lerp(PipColor, FlashingPipColor, colorTime);

            pip.SetPosition(pos + Custom.rotateVectorDeg(Vector2.one * rubberRadius, (i - 15) * (360f / PipSpritesLength)));
        }

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[PipSpritesLength];

        for (int i = 0; i < PipSpritesLength; i++)
        {
            sLeaser.sprites[i] = new FSprite("WormEye") { alpha = 0f };
        }

        base.InitiateSprites(sLeaser, rCam);
    }
}