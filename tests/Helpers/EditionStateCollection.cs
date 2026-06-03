#nullable enable

using Xunit;

namespace MMP.Herald.OSS.Tests.Helpers;

/// <summary>
/// Marker collection for any test class that mutates the process-global
/// edition slot OR capability set on <see cref="MMP.Herald.HeraldVersion"/>
/// — via <c>SetEdition</c>, <c>SetCurrentCapabilities</c> /
/// <c>ReplaceCurrentCapabilities</c>, <c>CapabilityResolver</c>, or the
/// internal <c>ResetForTesting</c> entrypoint.
///
/// <para>
/// CurrentEdition, CurrentCapabilities, and CapabilityResolver are static.
/// Classes that serialise only intra-class (in-class IDisposable reset, or a
/// private single-class collection) still race each other across class
/// boundaries when xUnit runs them in parallel: one class's ResetForTesting
/// can flip the slot back to Community mid-assertion in another. Joining one
/// DisableParallelization collection serialises every edition/cap mutator at
/// the class level without touching the rest of the suite.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EditionStateCollection : ICollectionFixture<EditionStateCollection>
{
    public const string Name = "EditionState";
}
