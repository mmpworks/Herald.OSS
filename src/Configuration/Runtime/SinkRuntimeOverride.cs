// Copyright (c) 2026 MMP LLC
// Licensed under the MIT License. See LICENSE in the project root.
#nullable enable

namespace MMP.Herald.Configuration.Runtime;

/// <summary>
/// One sink's persisted runtime override snapshot — the state the
/// dashboard's per-sink strip writes to the holders and the builder
/// keeps so a server reboot restores the same operator choice.
///
/// <para>Every field is nullable so a single-field PATCH can land
/// without resending the others. <c>null</c> on a field means "don't
/// change"; the consumer (the runtime apply path) merges the
/// non-null fields onto the previous snapshot. Defaults — when no
/// override has ever been recorded — are <see cref="SinkRunState.Live"/>,
/// no tees, no per-sink minimum-level gate.</para>
/// </summary>
/// <param name="RunState">
/// <c>disabled</c> / <c>live</c> / <c>test</c> string the JSON config
/// carries. <see cref="DefaultLoggingConfigurationMapper"/> parses it
/// to the enum on the way into the runtime layer; the serializer
/// writes it back as the same string. Stored as a string here so the
/// JSON layer doesn't depend on the enum's surface.
/// </param>
/// <param name="MinLevel">
/// Sink-level minimum-level key (<c>info</c>, <c>warn</c>, …) that
/// gates events for this sink only. Pipeline-level filtering is a
/// separate concept and is unaffected.
/// </param>
public sealed record SinkRuntimeOverride(
    string? RunState = null,
    bool? TeeLiveToFile = null,
    bool? TeeLiveToUrl = null,
    string? MinLevel = null)
{
    /// <summary>
    /// Returns a new override with non-null fields from
    /// <paramref name="incoming"/> replacing this snapshot's values.
    /// Used by the management API's PATCH funnel: a single-field
    /// request merges onto the existing per-sink state, leaving every
    /// untouched field alone.
    /// </summary>
    public SinkRuntimeOverride MergeWith(SinkRuntimeOverride incoming) => new(
        RunState:       incoming.RunState       ?? RunState,
        TeeLiveToFile:  incoming.TeeLiveToFile  ?? TeeLiveToFile,
        TeeLiveToUrl:   incoming.TeeLiveToUrl   ?? TeeLiveToUrl,
        MinLevel:       incoming.MinLevel       ?? MinLevel);
}
