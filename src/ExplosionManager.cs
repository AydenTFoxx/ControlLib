using System;
using MoreSlugcats;
using UnityEngine;

namespace ControlLib;

/// <summary>
///     The scavengers at ScavExplosions Inc. only provide the finest of destructive utilities!
/// </summary>
public static class ExplosionManager
{
    /// <summary>
    ///     Creates and immediately explodes a bomb of the given type at the target creature's position.<br/>
    ///     Nearly guaranteed to kill, or have your porls back.
    /// </summary>
    /// <param name="crit">The creature to be targeted by and blamed for the explosion.</param>
    /// <param name="bombType">The type of the explosive object to create.</param>
    /// <param name="onRealizedCallback">
    ///     If present, a method which is invoked with the created bomb as its argument,
    ///     before the actual explosion occurs.
    /// </param>
    public static void ExplodeCreature(Creature crit, AbstractPhysicalObject.AbstractObjectType bombType, Action<PhysicalObject>? onRealizedCallback = null) =>
        ExplodePos(crit, crit.room, crit.abstractCreature.pos, bombType, onRealizedCallback);

    /// <summary>
    ///     Creates and immediately explodes a bomb of the given type at the specified position.<br/>
    ///     May not kill, so you can't have your porls back.
    /// </summary>
    /// <param name="caller">If present, the creature to blame for the explosion itself.</param>
    /// <param name="room">The room where the explosion will occur.</param>
    /// <param name="pos">The position where the explosion will occur.</param>
    /// <param name="bombType">The type of the explosive object to create.</param>
    /// <param name="onRealizedCallback">
    ///     If present, a method which is invoked with the created bomb as its argument,
    ///     before the actual explosion occurs.
    /// </param>
    public static void ExplodePos(Creature? caller, Room room, WorldCoordinate pos, AbstractPhysicalObject.AbstractObjectType bombType, Action<PhysicalObject>? onRealizedCallback = null)
    {
        AbstractPhysicalObject abstractBomb = new(
            room.world,
            bombType,
            null,
            pos,
            room.world.game.GetNewID()
        );

        abstractBomb.RealizeInRoom();

        PhysicalObject? realizedBomb = abstractBomb.realizedObject;

        if (realizedBomb is null)
        {
            Main.Logger.LogWarning($"Failed to realize explosion for {caller}! Destroying abstract object.");

            abstractBomb.Destroy();
            return;
        }

        if (realizedBomb is Weapon weapon)
        {
            weapon.thrownBy = caller;
        }

        realizedBomb.CollideWithObjects = false;

        onRealizedCallback?.Invoke(realizedBomb);

        if (realizedBomb is ScavengerBomb scavBomb)
        {
            scavBomb.Explode(scavBomb.thrownClosestToCreature?.mainBodyChunk);
        }
        else if (realizedBomb is SingularityBomb singularity)
        {
            if (singularity.zeroMode)
                singularity.explodeColor = new Color(1f, 0.2f, 0.2f);

            singularity.Explode();
            singularity.Destroy();
        }
        else
        {
            Main.Logger.LogWarning($"{realizedBomb} is not a supported kaboom type; Destroying object.");

            realizedBomb?.Destroy();
            abstractBomb.Destroy();
        }
    }
}