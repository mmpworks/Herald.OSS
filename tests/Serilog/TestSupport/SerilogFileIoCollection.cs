#nullable enable

using Xunit;

namespace MMP.Herald.OSS.Tests.Serilog.TestSupport;

/// <summary>
/// Shared xUnit collection for the Lane-A tests that write real files on disk
/// (W1 rolling/retention, W8 net8 live run, and any future file-writer test).
/// Disabling parallelization keeps file-writing tests from racing each other's
/// temp directories and flush windows — each test still uses a unique temp dir,
/// but serialising them removes flush-timing flakiness under load.
///
/// <para>
/// Apply with <c>[Collection(SerilogFileIoCollection.Name)]</c> on the test class.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerilogFileIoCollection
{
    public const string Name = "SerilogFileIO";
}
