using RWCustom;
using UnityEngine;

namespace ControlLib.Telekinetics;

/// <summary>
///     A fully-independent implementation of <c>SB_A14</c> and <see cref="MoreSlugcats.HRKarmaShrine"/>'s "karmic increase" effect,
///     where the screen becomes golden for a moment, before smoothly fading to its usual palette.
/// </summary>
/// <remarks>
///     Unlike its predecessors, this object is purely visual, and does not affect the player's karma level.
/// </remarks>
public class FadingMeltLights : CosmeticSprite
{
    private RoomSettings.RoomEffect? meltEffect;
    private readonly float effectInitLevel;

    private bool initialized;
    private bool forcedMeltEffect;

    /// <summary>
    ///     The progress of the fade effect, ranging from <c>1f</c> (strongest gold tint) to <c>0f</c> (no gold tint, effect is removed).
    /// </summary>
    public float FadeProgress { get; private set; }

    public FadingMeltLights(Room room)
    {
        this.room = room;

        if (room is null) return;

        for (int i = 0; i < room.roomSettings.effects.Count; i++)
        {
            if (room.roomSettings.effects[i].type == RoomSettings.RoomEffect.Type.VoidMelt)
            {
                meltEffect = room.roomSettings.effects[i];
                effectInitLevel = meltEffect.amount;
                return;
            }
        }
    }

    public void Initialize()
    {
        if (initialized) return;

        room.PlaySound(SoundID.SB_A14);

        if (meltEffect is null)
        {
            meltEffect = new RoomSettings.RoomEffect(RoomSettings.RoomEffect.Type.VoidMelt, 1f, false);

            room.roomSettings.effects.Add(meltEffect);

            forcedMeltEffect = true;
        }

        for (int i = 0; i < 20; i++)
        {
            room.AddObject(new MeltLights.MeltLight(1f, room.RandomPos(), room, RainWorld.GoldRGB));
        }
        FadeProgress = 1f;

        initialized = true;
    }

    public override void Destroy()
    {
        base.Destroy();

        if (this is { room: not null, meltEffect: not null, forcedMeltEffect: true })
        {
            room.roomSettings.effects.Remove(meltEffect);

            forcedMeltEffect = false;
        }

        meltEffect = null;
    }

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (!initialized)
        {
            Initialize();
            return;
        }

        FadeProgress = Mathf.Max(0f, FadeProgress - 0.016666668f);
        meltEffect?.amount = Mathf.Lerp(effectInitLevel, 1f, Custom.SCurve(FadeProgress, 0.6f));

        if (FadeProgress <= 0f)
        {
            Destroy();
        }
    }

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);

        if (initialized)
        {
            rCam.ApplyPalette();
        }
    }
}