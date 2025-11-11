namespace ControlLib.Enums;

public static class AbstractObjectTypes
{
    public static AbstractPhysicalObject.AbstractObjectType? ObjectController;

    public static void RegisterValues() => ObjectController = new("ObjectController", register: true);

    public static void UnregisterValues()
    {
        ObjectController?.Unregister();
        ObjectController = null;
    }
}