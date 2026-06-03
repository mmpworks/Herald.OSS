#nullable enable

using Xunit;

namespace MMP.Herald.OSS.Tests.Helpers;

/// <summary>
/// Marker collection for any test class that mutates process-global state on
/// <see cref="MMP.Herald.Quick.HeraldHost.Default"/> — the static
/// <c>HeraldRegistry</c> facade, the <c>HeraldTenantScope</c> AsyncLocal,
/// or the <c>HeraldTenant.TenantAdmissionPolicy</c> delegate.
///
/// <para>
/// xUnit runs tests in DIFFERENT collections in parallel by default. Two
/// parallel test classes that both touch the same static fields can leak
/// into each other's observations (e.g., one class's policy delegate sees
/// registrations triggered by another class). Joining a single collection
/// serialises these tests at the class level, removing the cross-class race
/// without disabling parallelism for the rest of the suite.
/// </para>
///
/// <para>
/// Tests that exercise instance APIs only (constructing their own
/// <c>HeraldHost</c>) do not need this collection — instance state is
/// already isolated.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DefaultHostCollection : ICollectionFixture<DefaultHostCollection>
{
    public const string Name = "DefaultHost";
}
