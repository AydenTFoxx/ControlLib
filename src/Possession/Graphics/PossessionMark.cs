using UnityEngine;

namespace ControlLib.Possession.Graphics;

/// <summary>
///     A Mark of Communication-like sprite which appears above possessed creatures' heads.
/// </summary>
public class PossessionMark : PlayerAccessory
{
    private readonly float targetSize;
    private bool invalidated;

    /// <summary>
    ///     The creature this sprite will follow.
    /// </summary>
    public Creature Target { get; }

    /// <summary>
    ///     The owner of the accessory itself.
    /// </summary>
    public Player Owner { get; }

    /// <summary>
    ///     The position at which the sprite will be drawn.
    /// </summary>
    public Vector2 MarkPos => new Vector2(Target.firstChunk.pos.x, Target.firstChunk.pos.y + targetSize) - camPos;

    /// <summary>
    ///     Creates a new Possession Mark targeting the given creature and owned by the given player.
    /// </summary>
    /// <param name="target">The creature to be targeted.</param>
    /// <param name="owner">The owner of this accessory.</param>
    public PossessionMark(Creature target, Player owner) : base(owner)
    {
        targetSize = target.firstChunk.rad * 8;

        Target = target;
        Owner = owner;

        FollowCreature = target;
    }

    public void Invalidate() => invalidated = true;

    public override void Update(bool eu)
    {
        base.Update(eu);

        if (invalidated) return;

        if (Target.room is not null && Target.room != room)
        {
            TryRealizeInRoom(Target.room, Target.firstChunk.pos - camPos);
        }

        if (!Target.TryGetPossession(out Player possessor) || possessor != Owner)
        {
            invalidated = true;
        }
    }

    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        sLeaser.sprites = new FSprite[2];

        sLeaser.sprites[0] = new FSprite("Futile_White", true)
        {
            shader = rCam.game.rainWorld.Shaders["FlatLight"],
            scale = 2f,
            alpha = 0f
        };
        sLeaser.sprites[1] = new FSprite("pixel", true)
        {
            scale = 5f,
            alpha = 0f
        };

        base.InitiateSprites(sLeaser, rCam);
    }

    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        this.camPos = camPos;

        UpdateAlpha(!invalidated, maxDelta: 0.015f);

        if (alpha <= 0f)
        {
            if (invalidated)
                Destroy();
            return;
        }

        sLeaser.sprites[0].alpha = alpha * 2f * 0.2f;
        sLeaser.sprites[0].SetPosition(MarkPos);

        sLeaser.sprites[1].alpha = alpha * 2f;
        sLeaser.sprites[1].SetPosition(MarkPos);

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }
}