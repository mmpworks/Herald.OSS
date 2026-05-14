#nullable enable

using System;
using System.Threading;

namespace MMP.Herald.Quick;

/// <summary>
/// Ambient tenant id for the current async flow.
///
/// <para>
/// Request-scoped tenant resolution normally lives on <c>HttpContext.Items</c>,
/// but many of the Server and Dashboard helper methods do not take an
/// <c>HttpContext</c> parameter — they are called from deep inside endpoint
/// lambdas where passing context everywhere would be a 25+ site change.
/// <see cref="HeraldTenantScope"/> is the small, explicit seam that closes
/// that gap: ASP.NET's tenant middleware sets the scope at request entry,
/// registry helpers read it, and the async-flow semantics of
/// <see cref="AsyncLocal{T}"/> carry the value through every await without
/// leaking across requests.
/// </para>
///
/// <para>
/// Single-tenant deployments never set the scope; every read falls through
/// to <see cref="HeraldTenant.Default"/>, so legacy code paths keep working
/// unchanged. Set via <see cref="Push"/> (which returns an
/// <see cref="IDisposable"/> that restores the previous value on dispose)
/// or via direct assignment to <see cref="Current"/>.
/// </para>
/// </summary>
public static class HeraldTenantScope
{
    private static readonly AsyncLocal<string?> _current = new();

    /// <summary>
    /// Current tenant for this async flow. Returns <see cref="HeraldTenant.Default"/>
    /// when nothing has been set.
    ///
    /// <para>
    /// The setter runs <see cref="HeraldTenant.Normalize"/> so direct
    /// assignment produces the same stored value as <see cref="Push"/>.
    /// Without that, two paths to the same ambient tenant gave two
    /// different observable values — <c>Current = "Studio-A"</c> would
    /// store the mixed-case form while <c>Push("Studio-A")</c> stored
    /// <c>"studio-a"</c>, and code that read the scope to compare against
    /// a normalised key from the registry saw a mismatch.
    /// </para>
    /// </summary>
    public static string Current
    {
        get => _current.Value ?? HeraldTenant.Default;
        set => _current.Value = HeraldTenant.Normalize(value);
    }

    /// <summary>
    /// Establish a tenant for the remainder of the <c>using</c> block, then
    /// restore whatever was there before. Prefer this over direct assignment
    /// when writing tests so the scope does not bleed between test methods.
    /// </summary>
    public static IDisposable Push(string tenant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        var prior = _current.Value;
        _current.Value = HeraldTenant.Normalize(tenant);
        return new ScopeToken(prior);
    }

    private sealed class ScopeToken : IDisposable
    {
        private readonly string? _prior;
        internal ScopeToken(string? prior) { _prior = prior; }
        public void Dispose() => _current.Value = _prior;
    }
}
