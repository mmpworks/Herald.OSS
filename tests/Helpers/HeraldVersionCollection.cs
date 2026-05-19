#nullable enable

using Xunit;

namespace MMP.Herald.OSS.Tests.Helpers;

/// <summary>
/// Marker collection for any test class that mutates process-global state on
/// <see cref="MMP.Herald.HeraldVersion"/> — <c>CurrentEdition</c>,
/// <c>CurrentCapabilities</c>, <c>CapabilityResolver</c>, or the
/// <c>HeraldCapabilityGate.OnCapabilityChecked</c> event.
///
/// <para>
/// xUnit runs DIFFERENT test classes in parallel by default. Two parallel
/// classes that both touch the same static fields can leak into each other's
/// observations — most commonly when one class's <c>Dispose</c> calls
/// <c>HeraldVersion.ResetForTesting()</c> between another class's "publish
/// then assert" steps. Joining a single collection serialises these tests at
/// the class level, removing the cross-class race without disabling
/// parallelism for the rest of the suite.
/// </para>
///
/// <para>
/// This is the <see cref="HeraldVersion"/>-state analogue of
/// <see cref="DefaultHostCollection"/> (which serialises the
/// <see cref="MMP.Herald.Quick.HeraldHost"/> facade's mutating tests).
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class HeraldVersionCollection : ICollectionFixture<HeraldVersionCollection>
{
    public const string Name = "HeraldVersionState";
}
