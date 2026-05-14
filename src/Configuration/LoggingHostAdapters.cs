#nullable enable

using MMP.Herald.Output.Rich;
using MMP.Herald.Output.Writers;

namespace MMP.Herald.Configuration;
/// <summary>
/// Optional host adapter overrides.
/// Host-specific implementations inject their adapters here.
/// All defaults are host-agnostic.
/// </summary>
public sealed record LoggingHostAdapters(
    IRenderedLogOutputWriter? RichConsoleWriter = null,
    ILogFilePathResolver? FilePathResolver = null)
{
    public IRenderedLogOutputWriter ResolveRichConsoleWriter()
    {
        return RichConsoleWriter ?? new DefaultRichConsoleWriter();
    }

    public ILogFilePathResolver ResolveFilePathResolver()
    {
        return FilePathResolver ?? DefaultLogFilePathResolver.Instance;
    }
}