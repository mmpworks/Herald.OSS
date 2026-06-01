// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using MMP.Herald.Events;
using MMP.Herald.Formatting;
using MMP.Herald.Output.Writers;
using MMP.Herald.Pipeline.Kernel;
using MMP.Herald.Sinks;

namespace Herald.Sinks.File.Providers;

/// <summary>
/// File-specific <see cref="HeraldSinkBase"/> subclass that carries the
/// <see cref="INetworkSink"/> marker. The marker is what lets the
/// pipeline-construction advisory catch a file sink wired without
/// <c>WithAsync()</c> — disk I/O at delivery time can block the
/// producer thread the same way an HTTP POST can.
///
/// <para>
/// The generic <c>WriterLogger</c> in Herald.OSS is shared with console
/// and in-memory writers where <see cref="INetworkSink"/> does not
/// apply, so the marker can't ride upstream. This thin subclass mirrors
/// <c>WriterLogger</c>'s body for the file-writer case only.
/// </para>
/// </summary>
internal sealed class FileWriterLogger : HeraldSinkBase, INetworkSink, IDisposable
{
    private readonly ILogFormatter _formatter;
    private readonly ILineWriter _writer;

    public FileWriterLogger(ILogFormatter formatter, ILineWriter writer)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public override void Log(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var line = _formatter.Format(logEvent);
        _writer.WriteLine(line);
    }

    public override void Log(in LogEventBuffer buffer)
    {
        var line = _formatter.Format(in buffer);
        _writer.WriteLine(line);
    }

    public void Dispose()
    {
        if (_writer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
