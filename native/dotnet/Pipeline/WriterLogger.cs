#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Formatting;
using MMP.Herald.Output.Writers;
using MMP.Herald.Pipeline.Kernel;

namespace MMP.Herald.Pipeline;
/// <summary>
/// Glue between formatting and writing.
///
/// Implements <see cref="IKernelSink"/> so kernel-eligible pipelines can
/// dispatch a stack-allocated <see cref="LogEventBuffer"/> through the
/// formatter without materialising a heap <see cref="LogEvent"/>. The
/// kernel path skips the <c>new LogEvent(...)</c> allocation plus the
/// per-property heap allocs the legacy path pays.
/// </summary>
public sealed class WriterLogger : ILogger, IKernelSink, IComponentMetadata, IDisposable
{
    private readonly ILogFormatter _formatter;
    private readonly ILineWriter _writer;

    public WriterLogger(ILogFormatter formatter, ILineWriter writer)
    {
        _formatter = formatter;
        _writer = writer;
    }

    public void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var line = _formatter.Format(logEvent);
        _writer.WriteLine(line);
    }

    /// <summary>
    /// Kernel-path variant. The formatter's buffer overload reads fields
    /// straight from the stack-allocated buffer — no LogEvent allocation,
    /// no property-array materialisation. Built-in formatters (JSON,
    /// plain text) override the buffer overload; third-party formatters
    /// fall back to <see cref="ILogFormatter.Format(LogEvent)"/> via the
    /// interface default, which materialises at the boundary.
    /// </summary>
    public void Log(in LogEventBuffer buffer)
    {
        var line = _formatter.Format(in buffer);
        _writer.WriteLine(line);
    }

    /// <summary>
    /// Forwards disposal to the underlying writer when it is disposable. The
    /// long-lived <see cref="System.IO.FileStream"/> in the file writers is
    /// the resource that needs releasing; in-memory or stdout writers stay
    /// no-ops on dispose.
    /// </summary>
    public void Dispose()
    {
        if (_writer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    // -- IComponentMetadata --
    string IComponentMetadata.ComponentName => "writer";
    string IComponentMetadata.DisplayName => "Writer";
    string IComponentMetadata.Description => "Formats events and writes to an output destination.";
    string IComponentMetadata.Help => "Glue between ILogFormatter (serialization) and ILineWriter (output). Formats each event to a string and writes it. Implements IKernelSink so kernel-eligible pipelines skip the heap LogEvent allocation.";
    VendorInfo IComponentMetadata.Vendor => VendorInfo.MMP;
    Configuration.PipelineStepRules IComponentMetadata.Rules => Configuration.PipelineStepRules.Default;
    System.Collections.Generic.IReadOnlyList<Routing.SinkConfigField> IComponentMetadata.ConfigurationSchema => [];
}
