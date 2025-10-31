using System.Runtime.CompilerServices;
using BepInEx.Logging;
using ModLib.Logging;

namespace ControlLib;

public class LogWrapper(IMyLogger logger, LogLevel maxLogLevel) : IMyLogger
{
    public object GetLogSource() => logger.GetLogSource();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Log(object message) => Log(LogLevel.Debug, message);
    public void Log(LogLevel level, object data)
    {
        if (level is LogLevel.None || maxLogLevel is LogLevel.None || maxLogLevel < level)
        {
            return;
        }

        logger.Log(level, data);
    }

    public void LogDebug(object data) => logger.LogDebug(data);
    public void LogError(object data) => logger.LogError(data);
    public void LogFatal(object data) => logger.LogFatal(data);
    public void LogInfo(object data) => logger.LogInfo(data);
    public void LogMessage(object data) => logger.LogMessage(data);
    public void LogWarning(object data) => logger.LogWarning(data);
}