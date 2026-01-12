using System;
using System.Runtime.CompilerServices;
using RWCustom;
using UnityEngine;

namespace Possessions.Possession.Graphics;

/// <summary>
///     A cosmetic sprite which follows the player around, with simple tools for visual behavior.
/// </summary>
public abstract class PlayerAccessory : CosmeticSprite
{
    /// <summary>
    ///     The <see cref="PossessionManager"/> instance this accessory is loosely tied to.
    /// </summary>
    protected PossessionManager Manager { get; }

    /// <summary>
    ///     The player this accessory belongs to. If the player is destroyed, the accessory is also disposed of.
    /// </summary>
    protected readonly Player player;

    /// <summary>
    ///     The last camera position known by the accessory, used for setting its position outside of <see cref="IDrawable"/> methods.
    /// </summary>
    protected Vector2 camPos;

    /// <summary>
    ///     The current transparency of this accessory, usually updated via <see cref="UpdateAlpha"/>.
    /// </summary>
    protected float alpha;

    /// <summary>
    ///     If true, <see cref="alpha"/> has just reached the intended target value with <see cref="UpdateAlpha"/>.
    /// </summary>
    protected bool justReachedTargetAlpha = true;

    /// <summary>
    ///     The velocity of the accessory object; Preferred over <see cref="CosmeticSprite.vel"/> for smoothing functions such as <see cref="Vector2.SmoothDamp(Vector2, Vector2, ref Vector2, float)"/>.
    /// </summary>
    protected Vector2 velocity;

    /// <summary>
    ///     The lerp value used for lerping between one color and another with <see cref="UpdateColorLerp"/>.
    /// </summary>
    protected float colorTime;
    /// <summary>
    ///     The current direction of the color lerp from <see cref="UpdateColorLerp"/>.
    /// </summary>
    protected bool invertColorLerp;

    /// <summary>
    ///     The last known position for the Mark of Communication retrieved with <see cref="GetMarkPos"/>.
    /// </summary>
    private Vector2 lastMarkPos;

    /// <inheritdoc cref="camPos"/>
    public Vector2 CamPos => camPos;

    /// <summary>
    ///     The creature this accessory will be following. If set to null or unspecified, defaults to the player who owns the accessory itself.
    /// </summary>
    protected Creature? FollowCreature { get; set => field = value ?? player; }

    /// <summary>
    ///     Creates a new accessory instance with a reference to its owner's <see cref="PossessionManager"/> instance.
    /// </summary>
    /// <remarks>
    ///     This method is purely for convenience of use; <see cref="Manager"/> is never used by this class.
    /// </remarks>
    /// <param name="manager">The manager the accessory is loosely tied to; Its owner will also be the owner of the accessory.</param>
    public PlayerAccessory(PossessionManager manager)
    {
        Manager = manager;
        player = manager.GetPlayer();

        FollowCreature = player;

        player.room?.AddObject(this);
    }

    /// <summary>
    ///     Creates a new accessory directly tied to a given player.
    /// </summary>
    /// <param name="owner">The player who will own the accessory.</param>
    public PlayerAccessory(Player owner)
    {
        Manager = null!;
        player = owner;

        FollowCreature = owner;

        owner.room?.AddObject(this);
    }

    /// <summary>
    ///     Attempts to realize the accessory in the given room, also setting it at the provided position.
    /// </summary>
    /// <param name="newRoom">The room this accessory will be moved to.</param>
    /// <param name="newPos">The position this accessory will be set to.</param>
    public virtual void TryRealizeInRoom(Room newRoom, Vector2 newPos)
    {
        room?.RemoveObject(this);

        newRoom.AddObject(this);

        pos = newPos;
    }

    /// <summary>
    ///     Updates the accessory's <see cref="alpha"/> value at the provided rate, moving it towards <c>1f</c> or <c>0f</c> depending on <see cref="targetAlpha"/>.
    /// </summary>
    /// <param name="targetAlpha">If true, <see cref="alpha"/> will move towards <c>1f</c>. Otherwise, it'll move towards <c>0f</c>.</param>
    /// <param name="maxDelta">The absolute value by which <see cref="alpha"/> is incremented on every tick.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateAlpha(bool targetAlpha, float maxDelta = 0.02f) => UpdateAlpha(targetAlpha ? 1f : 0f, maxDelta);

    /// <summary>
    ///     Updates the accessory's <see cref="alpha"/> value, moving it towards <see cref="targetAlpha"/> at the provided rate.
    /// </summary>
    /// <param name="targetAlpha">The value <see cref="alpha"/> will move towards.</param>
    /// <param name="maxDelta">The absolute value by which <see cref="alpha"/> is incremented on every tick.</param>
    public void UpdateAlpha(float targetAlpha, float maxDelta = 0.02f)
    {
        if (targetAlpha != alpha)
        {
            alpha = Mathf.MoveTowards(alpha, targetAlpha, maxDelta);

            justReachedTargetAlpha = targetAlpha == alpha;
        }
        else
        {
            justReachedTargetAlpha = false;
        }
    }

    /// <summary>
    ///     Updates the accessory's fields used for lerping between colors.
    /// </summary>
    /// <param name="applyLerp">If <c>true</c>, <see cref="colorTime"/> bounces between <c>-1</c> and <c>1</c>. Otherwise, it smoothly moves towards <c>0</c>.</param>
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

    /// <inheritdoc/>
    public override void Update(bool eu)
    {
        base.Update(eu);

        if (player is null or { room: null, inShortcut: false })
        {
            Destroy();
            return;
        }

        if (FollowCreature?.room is not null && FollowCreature.room != room)
        {
            TryRealizeInRoom(FollowCreature.room, FollowCreature.mainBodyChunk.pos - camPos);
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override void DrawSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        this.camPos = camPos;

        base.DrawSprites(sLeaser, rCam, timeStacker, camPos);
    }

    /// <inheritdoc/>
    public override void InitiateSprites(RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        camPos = rCam.pos;

        AddToContainer(sLeaser, rCam, null);
    }

    /// <summary>
    ///     Retrieves the position at which the Mark of Communication is drawn for the owner of the accessory;
    ///     Used by most accessories which loosely hover above the player's head.
    /// </summary>
    /// <param name="camPos">The position of the room camera; Usually provided by <see cref="DrawSprites"/>, or <see cref="camPos"/>.</param>
    /// <param name="timeStacker">The time stacker for the current frame.</param>
    /// <returns>The position at which the Mark of Communication would be drawn on this frame.</returns>
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