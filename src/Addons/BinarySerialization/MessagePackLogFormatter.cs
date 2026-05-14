#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using MMP.Herald.Events;
using MMP.Herald.Formatting;
using MMP.Herald.Levels;

namespace MMP.Herald.Addons.BinarySerialization;

/// <summary>
/// Binary log formatter that encodes events in a MessagePack-compatible wire format.
/// Produces compact binary output suitable for IoT, edge, and bandwidth-constrained
/// environments where JSON overhead is unacceptable.
///
/// Wire format per event:
///   fixmap(8) {
///     "t": fixext8(timestamp as unix ms int64)
///     "l": fixstr(level key)
///     "r": uint8(level rank)
///     "c": fixstr(category)
///     "m": str(rendered message)
///     "T": str(message template)
///     "p": fixmap(N) { name: value, ... }
///     "x": fixmap(N) { key: value, ... }  (context, omitted if empty)
///   }
///
/// Implements both ILogFormatter (returns base64) and IUtf8LogFormatter (writes raw bytes).
///
/// Usage:
///   var formatter = new MessagePackLogFormatter(levelRegistry);
///   // As IUtf8LogFormatter (preferred - zero intermediate strings):
///   formatter.Format(logEvent, bufferWriter);
///   // As ILogFormatter (base64 string for text-based sinks):
///   var base64 = formatter.Format(logEvent);
/// </summary>
public sealed class MessagePackLogFormatter : ILogFormatter, IUtf8LogFormatter
{
    private readonly ILogLevelRegistry _levelRegistry;

    public MessagePackLogFormatter(ILogLevelRegistry levelRegistry) {
        _levelRegistry = levelRegistry ?? throw new ArgumentNullException(nameof(levelRegistry));
    }

    /// <summary>Format as base64-encoded binary (for text transports).</summary>
    public string Format(LogEvent logEvent) {
        var buffer = new ArrayBufferWriter<byte>(256);
        Format(logEvent, buffer);
        return Convert.ToBase64String(buffer.WrittenSpan);
    }

    /// <summary>Format directly to binary bytes (for binary transports).</summary>
    public void Format(LogEvent logEvent, IBufferWriter<byte> output) {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(output);

        // Count map entries: t, l, r, c, m, T, p + optional x
        var mapSize = logEvent.Context.Count > 0 ? 8 : 7;
        WriteFixMap(output, mapSize);

        // "t" -> timestamp (int64 unix milliseconds)
        WriteFixStr(output, "t");
        WriteInt64(output, logEvent.TimeUtc.ToUnixTimeMilliseconds());

        // "l" -> level key
        WriteFixStr(output, "l");
        WriteStr(output, logEvent.Level.Key);

        // "r" -> level rank
        WriteFixStr(output, "r");
        var registered = _levelRegistry.GetRegisteredLevel(logEvent.Level);
        WriteUInt8(output, (byte)registered.Rank);

        // "c" -> category
        WriteFixStr(output, "c");
        WriteStr(output, logEvent.Category.Value);

        // "m" -> rendered message
        WriteFixStr(output, "m");
        WriteStr(output, logEvent.Message);

        // "T" -> message template
        WriteFixStr(output, "T");
        WriteStr(output, logEvent.MessageTemplate);

        // "p" -> properties
        WriteFixStr(output, "p");
        WriteFixMap(output, logEvent.Properties.Count);
        foreach (var prop in logEvent.Properties)
        {
            WriteStr(output, prop.Name);
            WriteValue(output, prop.ResolvedValue);
        }

        // "x" -> context (optional)
        if (logEvent.Context.Count > 0)
        {
            WriteFixStr(output, "x");
            WriteFixMap(output, logEvent.Context.Count);
            foreach (var kvp in logEvent.Context)
            {
                WriteStr(output, kvp.Key);
                WriteValue(output, kvp.Value);
            }
        }
    }

    // ── MessagePack encoding primitives ─────────────────────────────────
    // Subset of MessagePack spec sufficient for log events.

    private static void WriteFixMap(IBufferWriter<byte> output, int count) {
        if (count <= 15)
        {
            var span = output.GetSpan(1);
            span[0] = (byte)(0x80 | count);
            output.Advance(1);
        }
        else
        {
            var span = output.GetSpan(3);
            span[0] = 0xde; // map 16
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(1), (ushort)count);
            output.Advance(3);
        }
    }

    private static void WriteFixStr(IBufferWriter<byte> output, string value) {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount <= 31)
        {
            var span = output.GetSpan(1 + byteCount);
            span[0] = (byte)(0xa0 | byteCount);
            Encoding.UTF8.GetBytes(value.AsSpan(), span.Slice(1));
            output.Advance(1 + byteCount);
        }
        else
        {
            WriteStr(output, value);
        }
    }

    private static void WriteStr(IBufferWriter<byte> output, string value) {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount <= 31)
        {
            var span = output.GetSpan(1 + byteCount);
            span[0] = (byte)(0xa0 | byteCount);
            Encoding.UTF8.GetBytes(value.AsSpan(), span.Slice(1));
            output.Advance(1 + byteCount);
        }
        else if (byteCount <= 255)
        {
            var span = output.GetSpan(2 + byteCount);
            span[0] = 0xd9; // str 8
            span[1] = (byte)byteCount;
            Encoding.UTF8.GetBytes(value.AsSpan(), span.Slice(2));
            output.Advance(2 + byteCount);
        }
        else if (byteCount <= 65535)
        {
            var span = output.GetSpan(3 + byteCount);
            span[0] = 0xda; // str 16
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(1), (ushort)byteCount);
            Encoding.UTF8.GetBytes(value.AsSpan(), span.Slice(3));
            output.Advance(3 + byteCount);
        }
        else
        {
            var span = output.GetSpan(5 + byteCount);
            span[0] = 0xdb; // str 32
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(1), (uint)byteCount);
            Encoding.UTF8.GetBytes(value.AsSpan(), span.Slice(5));
            output.Advance(5 + byteCount);
        }
    }

    private static void WriteInt64(IBufferWriter<byte> output, long value) {
        var span = output.GetSpan(9);
        span[0] = 0xd3; // int 64
        BinaryPrimitives.WriteInt64BigEndian(span.Slice(1), value);
        output.Advance(9);
    }

    private static void WriteUInt8(IBufferWriter<byte> output, byte value) {
        if (value <= 127)
        {
            var span = output.GetSpan(1);
            span[0] = value; // positive fixint
            output.Advance(1);
        }
        else
        {
            var span = output.GetSpan(2);
            span[0] = 0xcc; // uint 8
            span[1] = value;
            output.Advance(2);
        }
    }

    private static void WriteNil(IBufferWriter<byte> output) {
        var span = output.GetSpan(1);
        span[0] = 0xc0;
        output.Advance(1);
    }

    private static void WriteBool(IBufferWriter<byte> output, bool value) {
        var span = output.GetSpan(1);
        span[0] = value ? (byte)0xc3 : (byte)0xc2;
        output.Advance(1);
    }

    private static void WriteValue(IBufferWriter<byte> output, object? value) {
        switch (value)
        {
            case null:
                WriteNil(output);
                break;
            case bool b:
                WriteBool(output, b);
                break;
            case int i:
                WriteInt64(output, i);
                break;
            case long l:
                WriteInt64(output, l);
                break;
            case double d:
                WriteDouble(output, d);
                break;
            case float f:
                WriteDouble(output, f);
                break;
            case string s:
                WriteStr(output, s);
                break;
            case Exception ex:
                WriteStr(output, $"{ex.GetType().Name}: {ex.Message}");
                break;
            default:
                WriteStr(output, value.ToString() ?? "");
                break;
        }
    }

    private static void WriteDouble(IBufferWriter<byte> output, double value) {
        var span = output.GetSpan(9);
        span[0] = 0xcb; // float 64
        BinaryPrimitives.WriteDoubleBigEndian(span.Slice(1), value);
        output.Advance(9);
    }
}
