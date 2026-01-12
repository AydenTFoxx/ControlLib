using System;
using System.Collections.Generic;
using System.Linq;
using Possessions.Possession;
using RainMeadow;
using RainMeadow.Generics;

namespace Possessions.Meadow;

public class SerializableDataSnapshot : Serializer.ICustomSerializable
{
    private PossessionManager.ManagerDataSnapshot Data;

    private OnlineEntity.EntityId? OnlineOwnerId;
    private DynamicOrderedEntityIDs? OnlineCreaturePossessions;
    private DynamicOrderedEntityIDs? OnlineItemPossessions;

    public SerializableDataSnapshot() { }
    public SerializableDataSnapshot(PossessionManager.ManagerDataSnapshot data)
    {
        Data = data;

        OnlineOwnerId = Data.player.abstractCreature.GetOnlineCreature()?.id;

        OnlineCreaturePossessions = LocalToOnlineIds(Data.MyPossessions);
        OnlineItemPossessions = LocalToOnlineIds(Data.PossessedItems);
    }

    public void CustomSerialize(Serializer serializer)
    {
        if (OnlineOwnerId is null || OnlineCreaturePossessions is null || OnlineItemPossessions is null)
            throw new InvalidOperationException($"Cannot serialize an object with null values. Invalid field: {(OnlineOwnerId is null ? nameof(OnlineOwnerId) : OnlineCreaturePossessions is null ? nameof(OnlineCreaturePossessions) : nameof(OnlineItemPossessions))}");

        serializer.Serialize(ref OnlineCreaturePossessions);
        serializer.Serialize(ref OnlineItemPossessions);

        serializer.Serialize(ref OnlineOwnerId);

        serializer.Serialize(ref Data.IsAttunedSlugcat);
        serializer.Serialize(ref Data.IsHardmodeSlugcat);
        serializer.Serialize(ref Data.IsSofanthielSlugcat);

        serializer.Serialize(ref Data.MaxPossessionTime);

        serializer.Serialize(ref Data.PossessionTime);
        serializer.Serialize(ref Data.PossessionCooldown);

        serializer.Serialize(ref Data.OnMindBlastCooldown);
        serializer.Serialize(ref Data.SpritesNeedReset);
    }

    public PossessionManager.ManagerDataSnapshot ToDataSnapshot()
    {
        if (OnlineOwnerId is null || OnlineCreaturePossessions is null || OnlineItemPossessions is null)
            throw new InvalidOperationException($"Cannot de-serialize an object with null values. Invalid field: {(OnlineOwnerId is null ? nameof(OnlineOwnerId) : OnlineCreaturePossessions is null ? nameof(OnlineCreaturePossessions) : nameof(OnlineItemPossessions))}");

        if (OnlineManager.lobby.activeEntities.OfType<OnlineCreature>().FirstOrDefault(oc => oc.id == OnlineOwnerId)?.realizedCreature is not Player player)
            throw new InvalidOperationException($"Could not retrieve player instance with id: {OnlineOwnerId}");

        Data.player = player;

        Data.MyPossessions = [.. OnlineIdsToLocal<Creature>(OnlineCreaturePossessions.list)];
        Data.PossessedItems = [.. OnlineIdsToLocal<PhysicalObject>(OnlineItemPossessions.list)];

        return Data;
    }

    private static DynamicOrderedEntityIDs LocalToOnlineIds<T>(IList<T> list) where T : PhysicalObject
    {
        DynamicOrderedEntityIDs result = new();

        foreach (T item in list)
        {
            OnlineEntity.EntityId? onlineId = item.abstractPhysicalObject.GetOnlineObject()?.id;

            if (onlineId is null) continue;

            result.list.Add(onlineId);
        }

        return result;
    }

    private static List<T> OnlineIdsToLocal<T>(IList<OnlineEntity.EntityId> list) where T : PhysicalObject
    {
        List<T> result = [];

        foreach (OnlineEntity.EntityId id in list)
        {
            if (OnlineManager.lobby.activeEntities.OfType<OnlinePhysicalObject>().FirstOrDefault(oc => oc.id == id)?.apo.realizedObject is not T obj) continue;

            result.Add(obj);
        }

        return result;
    }
}