// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System.Text.Json;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Lifts the v1 / v2 sink-config envelope into a single shape every axis
/// can read uniformly. Replaces the inline envelope check that was
/// duplicated across every sink-config call site in
/// <see cref="HeraldManagementApi"/>.
///
/// <para>The wire formats:</para>
/// <list type="bullet">
///   <item><b>v1 (legacy flat).</b> <c>config: { uri: "...", minLevel: "info" }</c>.
///     <c>minLevel</c> rides next to the sink's own fields.</item>
///   <item><b>v2 (envelope).</b> <c>config: { properties: { uri: "..." },
///     minLevel: "info" }</c>. The sink-owned fields live inside
///     <c>properties</c>; <c>minLevel</c> stays on the outer envelope as
///     pipeline metadata.</item>
/// </list>
///
/// <para>Callers receive the inner properties element (or the original
/// element under v1), the outer <c>minLevel</c> string (or <c>null</c>),
/// and a flag indicating which contract shipped so a caller that needs
/// to re-read outer fields under v2 can branch deterministically.</para>
/// </summary>
internal static class SinkConfigEnvelope
{
    /// <summary>
    /// The lifted view of a sink's <c>config</c> JSON element. <see cref="Properties"/>
    /// is always the element a sink-config consumer should iterate; under v1 it
    /// is the original <paramref name="configEl"/>, under v2 it is the inner
    /// <c>properties</c> object.
    /// </summary>
    public readonly record struct LiftedEnvelope(
        JsonElement Properties,
        string? MinLevel,
        bool IsV2Envelope);

    /// <summary>
    /// Pull the v1/v2 envelope normalization out of inline code. Reads
    /// the <c>minLevel</c> string off the outer envelope (both contracts
    /// keep it there) and, when a <c>properties</c> object is present,
    /// returns that as the canonical inner element so sink readers loop
    /// once without per-call branches.
    /// </summary>
    /// <param name="configEl">
    /// The sink's <c>config</c> element from the commit payload. Must be
    /// a JSON object — callers gate on <c>ValueKind</c> before invoking.
    /// </param>
    public static LiftedEnvelope Lift(JsonElement configEl)
    {
        var properties = configEl;
        var isV2Envelope = false;
        if (configEl.TryGetProperty("properties", out var propsEl)
            && propsEl.ValueKind == JsonValueKind.Object)
        {
            properties = propsEl;
            isV2Envelope = true;
        }

        string? minLevel = null;
        if (configEl.TryGetProperty("minLevel", out var minLevelEl)
            && minLevelEl.ValueKind == JsonValueKind.String)
        {
            minLevel = minLevelEl.GetString();
        }

        return new LiftedEnvelope(properties, minLevel, isV2Envelope);
    }
}
