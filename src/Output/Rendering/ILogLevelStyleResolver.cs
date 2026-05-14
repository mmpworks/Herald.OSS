#nullable enable

using MMP.Herald.Levels;

namespace MMP.Herald.Output.Rendering;
/// <summary>
/// Resolves presentation style for a log level.
/// </summary>
public interface ILogLevelStyleResolver
{
    LogLevelStyle Resolve(LogLevel level);
}