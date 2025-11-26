using RWCustom;
using UnityEngine;

namespace ControlLib.Possession.Graphics;

public class PossessionTimer(PossessionManager manager) : PlayerAccessory(manager)
{
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
    private readonly int PipSpritesLength = manager.PossessionTimePotential / 30;

    private float rubberRadius;

    private bool IsPlayerVisible => player is { room: not null, dead: false };

    private bool ShouldShowPips => IsPlayerVisible && Manager.PossessionTime < Manager.MaxPossessionTime;

    private Color FlashingPipColor =>
        Manager.TargetSelector?.State is TargetSelector.QueryingState
            ? PipColor == Color.white
                ? RainWorld.GoldRGB
                : Color.white
            : Color.red;

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        lastPos = pos;
        pos = FollowMark is not null
            ? FollowMark.MarkPos
            : IsPlayerVisible
                ? Vector2.SmoothDamp(lastPos, GetMarkPos(camPos, timeStacker), ref velocity, 0.05f)
                : GetMarkPos(camPos, timeStacker);

        targetAlpha = Mathf.Clamp01(targetAlpha + (((ShouldShowPips ? 1f : 0f) - alpha) * 0.05f));

        if (UpdateAlpha() <= 0f) return;

        float pipScale = Manager.MaxPossessionTime / PipSpritesLength;

        for (int m = 0; m < sLeaser.sprites.Length; m++)
        {
            FSprite pip = sLeaser.sprites[m];

            float num22 = pipScale * m;
            float num23 = pipScale * (m + 1);
            pip.scale = Manager.PossessionTime <= num22
                ? 0f
                : Manager.PossessionTime >= num23
                    ? 1f
                    : (Manager.PossessionTime - num22) / pipScale;
        }

        UpdateColorLerp(Manager.LowPossessionTime || Manager.TargetSelector?.State is TargetSelector.QueryingState);

        float radius = Manager.IsPossessing ? 12f : 6f;
        rubberRadius += (radius - rubberRadius) * 0.045f;

        if (rubberRadius < 5f)
        {
            rubberRadius = radius;
        }

        for (int i = 0; i < sLeaser.sprites.Length; i++)
        {
            FSprite pip = sLeaser.sprites[i];

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
            sLeaser.sprites[i] = new FSprite("WormEye");
        }

        base.InitiateSprites(sLeaser, rCam);
    }
}