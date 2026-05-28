#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Templating;

namespace MMP.Herald.Events;

/// <summary>
/// Shared eager-resolution primitive used by both
/// <see cref="LogEventFactory"/> and <see cref="DeferredLogEventFactory"/>
/// at the L2 layer of the async-sink cross-tenant PII fix (0.10.2).
///
/// <para>
/// After enrichers run and before the factory freezes the immutable
/// <see cref="LogEvent"/>, this helper walks the property list and:
/// </para>
/// <list type="bullet">
///   <item>For any property whose <see cref="LogProperty.Value"/> is a
///         <see cref="Func{T}"/>, invokes the factory on the current
///         (producer) thread and replaces the property with the resolved
///         value.</item>
///   <item>For properties tagged with
///         <see cref="LogPropertyVisibility.PiiSensitive"/> visibility,
///         additionally forces the resolved value through <c>.ToString()</c>
///         so the value rides downstream as a string — closing the
///         transitive-<c>ToString()</c>-at-drain-thread cross-tenant
///         vector.</item>
/// </list>
///
/// <para>
/// Exception fence: a throwing lazy factory produces a descriptive
/// fallback string (mirroring <see cref="LogProperty.ResolvedValue"/>'s
/// internal try-catch) instead of failing the whole event. Logging
/// must never crash on a bad property.
/// </para>
/// </summary>
internal static class LogPropertyEagerResolver
{
    /// <summary>
    /// Walk a mutable property list in place and resolve any lazy factory
    /// values on the calling (producer) thread. PII-sensitive properties
    /// additionally pass the resolved value through <c>.ToString()</c>.
    /// </summary>
    public static void ResolveInPlace(List<LogProperty> properties)
    {
        if (properties is null) return;
        for (var i = 0; i < properties.Count; i++)
        {
            var p = properties[i];
            var isPii = p.VisibilityOrDefault == LogPropertyVisibility.PiiSensitive;
            var isLazy = p.Value is Func<object?>;
            if (!isLazy && !isPii) continue;

            var resolved = isLazy
                ? InvokeFactory((Func<object?>)p.Value!, p.Name)
                : p.Value;

            // PII force-to-string lands on every PiiSensitive property,
            // whether lazy or not — the contract is "no Func crosses the
            // channel, and no PII reference rides as anything but a
            // string." A null PII value passes through as null.
            if (isPii && resolved is not null)
            {
                resolved = resolved.ToString();
            }

            properties[i] = new LogProperty(
                p.Name, resolved, p.CaptureMode, p.Format, p.Visibility);
        }
    }

    private static object? InvokeFactory(Func<object?> factory, string propertyName)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            return $"[Lazy property '{propertyName}' threw {ex.GetType().Name}: {ex.Message}]";
        }
    }
}
