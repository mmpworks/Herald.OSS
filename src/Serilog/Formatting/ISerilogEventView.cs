#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Formatting;

/// <summary>
/// Narrow read-only view the output-template renderer (P3 Tasks 2–6) and the CLEF
/// formatters (P3 Tasks 7–8) consume.
///
/// <para>
/// <b>Purpose.</b> Decouples the renderer from the concrete P1 mirror type
/// (<see cref="MMP.Herald.Serilog.Events.LogEvent"/>) so Tasks 2–6 can proceed
/// against a test double (<c>FakeSerilogEventView</c>) before the mirror is fully
/// exercised. The bridge from the real mirror to this interface is a one-file swap.
/// </para>
///
/// <para>
/// <b>HIGH-1 pre-mortem note.</b> <see cref="TemplateHoleNames"/> exposes the
/// parsed hole names from the message template (not a raw string). The
/// <c>{Properties}</c> residual selector must intersect both the output-template hole
/// set and this set to exclude already-rendered properties, matching Serilog's
/// documented behaviour. If this member were a raw template string the selector
/// would need its own parser, which could diverge from the P2 parser.
/// </para>
/// </summary>
public interface ISerilogEventView
{
    /// <summary>
    /// Event timestamp, in the same representation that Serilog stores
    /// (local time by convention — see OD-3 open decision before Task 4 merges).
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>The log level for this event.</summary>
    LogEventLevel Level { get; }

    /// <summary>
    /// The original message template string, holes intact.
    /// Example: <c>"User {UserId} logged in"</c>.
    /// </summary>
    string MessageTemplate { get; }

    /// <summary>
    /// The fully rendered message — holes replaced by their formatted values.
    /// Example: <c>"User 42 logged in"</c>.
    /// </summary>
    string RenderedMessage { get; }

    /// <summary>
    /// The projected property tree. Keys are the hole names from the message
    /// template; values are the fully-typed Serilog value nodes
    /// (<see cref="ScalarValue"/>, <see cref="StructureValue"/>,
    /// <see cref="SequenceValue"/>, <see cref="DictionaryValue"/>).
    ///
    /// <para>
    /// Used by the <c>{Properties}</c> token renderer and the <c>:lj</c> specifier
    /// to walk the value tree.
    /// </para>
    /// </summary>
    IReadOnlyDictionary<string, LogEventPropertyValue> Properties { get; }

    /// <summary>Optional exception attached to the event.</summary>
    Exception? Exception { get; }

    /// <summary>
    /// The parsed hole names from <see cref="MessageTemplate"/>, in declaration
    /// order. Example: for <c>"User {UserId} logged in"</c> this is
    /// <c>["UserId"]</c>.
    ///
    /// <para>
    /// The <c>{Properties}</c> residual selector uses this list to exclude
    /// properties that are already rendered by the message token, matching
    /// Serilog's documented "exclude message-template holes" rule (HIGH-1
    /// pre-mortem finding). Exposing the parsed names here avoids a second
    /// parser in the renderer.
    /// </para>
    /// </summary>
    IReadOnlyList<string> TemplateHoleNames { get; }
}
