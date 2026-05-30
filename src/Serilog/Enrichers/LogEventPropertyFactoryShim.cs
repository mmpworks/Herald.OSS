#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using MMP.Herald.Events;
using MMP.Herald.Templating;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Enrichers;

/// <summary>
/// Implements <see cref="ILogEventPropertyFactory"/> on behalf of an active
/// <see cref="LogEventEnrichmentContext"/>.
///
/// <para>
/// <b>Two responsibilities:</b>
/// <list type="number">
///   <item>
///     Forward the add to the native enrichment context so the property
///     appears on the finalized <see cref="MMP.Herald.Events.LogEvent"/>.
///   </item>
///   <item>
///     Return a Serilog-shaped <see cref="LogEventProperty"/> so the user
///     enricher can call
///     <see cref="MMP.Herald.Serilog.Events.LogEvent.AddOrUpdateProperty"/>
///     if needed (matching Serilog's own enricher contract).
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>destructureObjects routing (S2 contract):</b>
/// <c>destructureObjects:true</c> maps to
/// <see cref="LogPropertyCaptureMode.Destructure"/> on the native
/// <see cref="LogProperty"/>. This means the projector will walk the object
/// reflectively at projection time, producing a
/// <see cref="StructureValue"/> rather than a
/// <see cref="ScalarValue"/>. Passing <c>false</c> (the default) stores a raw
/// <c>object?</c> that serialises as a scalar, matching Serilog's non-destructure path.
/// </para>
///
/// <para>
/// <b>Return value construction:</b>
/// The Serilog value tree for the returned <see cref="LogEventProperty"/> is
/// built using the same <see cref="ILogEventPropertyValueFactory"/> that
/// <see cref="LogEventValueProjector"/> uses, so the shape is identical to
/// what a consumer sink would see when it reads
/// <c>logEvent.Properties["name"]</c>.
/// </para>
/// </summary>
internal sealed class LogEventPropertyFactoryShim(LogEventEnrichmentContext context)
    : MMP.Herald.Serilog.Events.ILogEventPropertyFactory
{
    // Reuse the same factory the projector uses so value shapes are consistent.
    private static readonly ILogEventPropertyValueFactory _valueFactory =
        LogEventValueProjector.DefaultValueFactory;

    /// <inheritdoc/>
    [RequiresUnreferencedCode(
        "Value projection uses reflection on arbitrary user types.")]
    public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Forward to the native enrichment context.
        // destructureObjects:true → Destructure capture mode so the projector
        //                           builds a StructureValue (not a ScalarValue).
        // destructureObjects:false → null capture mode → projector defaults to scalar.
        var captureMode = destructureObjects
            ? LogPropertyCaptureMode.Destructure
            : (LogPropertyCaptureMode?)null;

        context.AddProperty(name, value, captureMode);

        // Build the Serilog-shaped return value using the projector's factory.
        var propertyValue = _valueFactory.CreatePropertyValue(value, destructureObjects);
        return new LogEventProperty(name, propertyValue);
    }
}
