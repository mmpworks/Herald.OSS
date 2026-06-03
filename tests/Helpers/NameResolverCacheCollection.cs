#nullable enable

using Xunit;

namespace MMP.Herald.OSS.Tests.Helpers;

/// <summary>
/// Marker collection for test classes that reset the process-global
/// <see cref="MMP.Herald.Templating.NameResolverCache"/> in their setup AND/OR
/// assert exact cache miss/hit counts.
///
/// <para>
/// <b>Why this is separate from the runtime-notice routing change.</b> The
/// per-host runtime-message channel
/// (docs/design/announcement-channel-per-host-routing.md) isolates each test's
/// NOTICE BUFFER, which is what retired these classes' <c>DefaultHostCollection</c>
/// membership. But <c>NameResolverCache</c> is a different shared static: a class
/// that calls <c>NameResolverCache.Reset()</c> clears the WHOLE process cache, so
/// a sibling class resetting it between another class's first-call-miss and
/// second-call-hit assertions turns the second call into a miss too
/// (CacheMisses 1 -> 2). Channel routing cannot isolate this — it is the same
/// class of genuinely-host-less shared static the cap-hit corrals guard. These
/// classes therefore stay serialised against each other on this dedicated
/// collection, which is narrower than <c>DefaultHostCollection</c> (it serialises
/// only the cache mutators, not every HeraldHost.Default toucher).
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NameResolverCacheCollection : ICollectionFixture<NameResolverCacheCollection>
{
    public const string Name = "NameResolverCache";
}
