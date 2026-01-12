namespace Possessions.Enums;

/// <summary>
///     Static registry of abstract physical object types, used for realization of physical object instances.
/// </summary>
public static class AbstractObjectTypes
{
    /// <summary>
    ///     An abstract object which becomes a <see cref="Telekinetics.ObjectController"/> instance when realized.
    /// </summary>
    public static AbstractPhysicalObject.AbstractObjectType? ObjectController;

    /// <summary>
    ///     Registers all values of the registry class to the game.
    /// </summary>
    public static void RegisterValues() => ObjectController = new("ObjectController", register: true);

    /// <summary>
    ///     Unregisters all values of the registry class from the game.
    /// </summary>
    public static void UnregisterValues()
    {
        ObjectController?.Unregister();
        ObjectController = null;
    }
}