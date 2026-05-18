#nullable enable

using System;
using MMP.Herald.Templating;

namespace MMP.Herald.Quick;

// Property naming-policy setters and accessors. Extracted from
// QuickLogBuilder.With.cs (principal-review queue #19) along Rosanne's
// seam map: zero coupling to the rest of the builder, isolated state
// (_namingPolicy + _suppressNamingPolicyAnnouncement live on
// QuickLogBuilder.cs and are read here through the partial).
public sealed partial class QuickLogBuilder
{
    // -- Property naming policy --

    /// <summary>
    /// Set the property-naming policy applied by the typed-args runtime
    /// dispatch path. The Herald.OSS 1.0+ default is
    /// <see cref="PropertyNamingPolicy.Pascal"/> — template tokens drive
    /// property names, matching Serilog / MEL / NLog convention. Opt in to
    /// <see cref="PropertyNamingPolicy.Camel"/> for camelCase property keys
    /// (JavaScript / JSON-API downstreams), or
    /// <see cref="PropertyNamingPolicy.Snake"/> for OpenTelemetry-friendly
    /// snake_case output.
    ///
    /// <para>
    /// Custom policies (e.g. a future <c>KebabCasePolicy</c>) work too —
    /// register them with <c>NamingPolicyRegistry.Register</c> before
    /// any <c>Reload(json)</c> call that names them by id.
    /// </para>
    /// </summary>
    /// <param name="policy">The naming policy to apply. Use the static
    /// accessors on <see cref="PropertyNamingPolicy"/> for built-ins.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="policy"/> is null.</exception>
    public QuickLogBuilder WithNamingPolicy(IPropertyNamingPolicy policy)
    {
        if (policy is null) throw new ArgumentNullException(nameof(policy));
        _namingPolicy = policy;
        return this;
    }

    /// <summary>
    /// Read the currently-configured naming policy. Returns
    /// <see cref="PropertyNamingPolicy.Pascal"/> when no explicit policy
    /// has been set — that's the default the eventual <c>StructuredLogger</c>
    /// will receive.
    /// </summary>
    public IPropertyNamingPolicy GetNamingPolicy()
    {
        return _namingPolicy ?? PropertyNamingPolicy.Pascal;
    }

    /// <summary>
    /// Carry-forward hook used by <c>QuickLogResult.RebuildFrom</c>: writes
    /// the supplied policy onto the builder only when the builder has not
    /// explicitly chosen one. Lets a rebuild preserve the live pipeline's
    /// policy when the caller's builder doesn't override it, while still
    /// honouring an explicit <see cref="WithNamingPolicy"/> on that
    /// builder. Spec invariant: silent default-flip on hot-reload is the
    /// pattern the project has scars from (see
    /// <c>feedback_hot_reload_gate_preservation</c>).
    /// </summary>
    internal void SetNamingPolicyIfUnset(IPropertyNamingPolicy fallback)
    {
        if (fallback is null) throw new ArgumentNullException(nameof(fallback));
        _namingPolicy ??= fallback;
    }

    /// <summary>
    /// Suppress the one-shot "Active naming policy: ..." Info event that
    /// Herald emits on first dispatch (Phase 5 / v1.0). The announcement
    /// is per-<see cref="MMP.Herald.Pipeline.StructuredLogger"/> instance
    /// and fires through the logger's own sinks, so it lands wherever
    /// regular events do — call this if your service treats Info as
    /// load-bearing structured output and the announcement noise would
    /// pollute it.
    ///
    /// <para>
    /// Equivalent process-wide alternative: set the environment variable
    /// <c>HERALD_NAMINGPOLICY_QUIET=1</c> before the host starts.
    /// </para>
    /// </summary>
    public QuickLogBuilder SuppressNamingPolicyAnnouncement()
    {
        _suppressNamingPolicyAnnouncement = true;
        return this;
    }

    /// <summary>
    /// Internal accessor read by <c>QuickLogBuilder.Build()</c> when it
    /// threads suppression into the freshly built
    /// <see cref="MMP.Herald.Pipeline.StructuredLogger"/>.
    /// </summary>
    internal bool IsNamingPolicyAnnouncementSuppressed => _suppressNamingPolicyAnnouncement;
}
