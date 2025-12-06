using System.Collections.Generic;
using RainMeadow;

namespace ControlLib.Meadow;

public abstract class SerializableDictionary<TOnlineKey, TOnlineValue, TLocalKey, TLocalValue> : Serializer.ICustomSerializable
{
    public Dictionary<TOnlineKey, TOnlineValue> collection = [];

    public Dictionary<TLocalKey, TLocalValue> LocalDict => OnlineIdsToLocal(collection);

    public SerializableDictionary() { }

    public SerializableDictionary(Dictionary<TLocalKey, TLocalValue> dict)
    {
        collection = LocalToOnlineIds(dict);
    }

    public SerializableDictionary(Dictionary<TOnlineKey, TOnlineValue> dict)
    {
        collection = dict;
    }

    public abstract void CustomSerialize(Serializer serializer);

    public abstract Dictionary<TOnlineKey, TOnlineValue> LocalToOnlineIds(Dictionary<TLocalKey, TLocalValue> dict);

    public abstract Dictionary<TLocalKey, TLocalValue> OnlineIdsToLocal(Dictionary<TOnlineKey, TOnlineValue> dict);
}