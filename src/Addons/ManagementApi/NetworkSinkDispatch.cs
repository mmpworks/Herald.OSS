// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Quick;
using MMP.Herald.Services;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Single source of truth for the per-kind dispatch the management API
/// uses when applying or restoring a network / integration sink.
///
/// <para>The shape covers two parallel paths that previously held two
/// copies of the same six branches:</para>
/// <list type="bullet">
///   <item><see cref="HeraldManagementApi"/>'s <c>ApplySinkConfig</c> —
///     reads URI / Host / Port / MinLevel out of the dashboard's commit
///     payload and calls the matching <c>QuickLogBuilder.WithXxxSink</c>.</item>
///   <item><see cref="HeraldManagementApi.RestoreBuilderFromConfig"/> —
///     reads the same fields off a saved <c>JsonLogSinkConfig</c> on
///     boot and calls the same builder method.</item>
/// </list>
///
/// <para>The two tables below are the only place that maps a sink kind
/// to its builder method. Adding a new URI-style sink means one row in
/// <see cref="UriSinks"/> (or <see cref="HostPortSinks"/>) and the
/// builder gets the new sink wired through both code paths for free.</para>
/// </summary>
internal static class NetworkSinkDispatch
{
    /// <summary>
    /// Sinks the dashboard configures with (uri, minLevel, headers) — the
    /// most common shape. Each value is the builder method that takes
    /// <c>(string uri, string? minLevel, IReadOnlyDictionary&lt;string,string&gt;? headers)</c>.
    /// Headers are optional; sinks that don't support custom headers ignore
    /// the parameter (Slack webhook drops it because the URL itself encodes
    /// the destination token).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Action<QuickLogBuilder, string, string?, IReadOnlyDictionary<string, string>?>> UriSinks
        = new Dictionary<string, Action<QuickLogBuilder, string, string?, IReadOnlyDictionary<string, string>?>>(StringComparer.OrdinalIgnoreCase)
        {
            [KnownSinkKinds.HttpJson]       = (b, uri, lvl, hdr) => b.WithHttpJsonSink(uri, lvl, hdr),
            [KnownSinkKinds.Elasticsearch]  = (b, uri, lvl, hdr) => b.WithElasticsearchSink(uri, lvl, hdr),
            [KnownSinkKinds.SlackWebhook]   = (b, uri, lvl, _)   => b.WithSlackWebhookSink(uri, lvl),
            [KnownSinkKinds.GenericWebhook] = (b, uri, lvl, hdr) => b.WithWebhookSink(uri, lvl, hdr),
            [KnownSinkKinds.OtlpJson]       = (b, uri, lvl, hdr) => b.WithOtlpJsonSink(uri, lvl, hdr),
            [KnownSinkKinds.OtlpProtobuf]   = (b, uri, lvl, hdr) => b.WithOtlpProtobufSink(uri, lvl, hdr),
        };

    /// <summary>
    /// Sinks the dashboard configures with (host, port, minLevel). The
    /// default-port column carries the protocol-conventional default the
    /// builder method falls back to when the JSON omits the port.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, HostPortSinkSpec> HostPortSinks
        = new Dictionary<string, HostPortSinkSpec>(StringComparer.OrdinalIgnoreCase)
        {
            [KnownSinkKinds.TcpJsonLine] = new(5000, (b, host, port, lvl) => b.WithTcpJsonLineSink(host, port, lvl)),
            [KnownSinkKinds.UdpJsonLine] = new(514,  (b, host, port, lvl) => b.WithUdpJsonLineSink(host, port, lvl)),
        };

    public readonly record struct HostPortSinkSpec(int DefaultPort, Action<QuickLogBuilder, string, int, string?> Apply);

    /// <summary>
    /// Read the optional <c>headers</c> entry out of a sink's properties bag
    /// and adapt it to the <c>IReadOnlyDictionary&lt;string,string&gt;</c>
    /// shape the builder methods expect. The JSON converter parses nested
    /// objects as <c>Dictionary&lt;string, object?&gt;</c>; this helper
    /// projects the values to strings (skipping null / non-string entries
    /// silently so a malformed header value does not crash boot).
    /// Returns null when there is no <c>headers</c> entry or it is empty
    /// — sinks that don't use headers see exactly the pre-existing shape.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ReadHeaders(IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null) return null;
        if (!properties.TryGetValue("headers", out var raw) || raw is null) return null;
        if (raw is not IReadOnlyDictionary<string, object?> nested || nested.Count == 0) return null;

        var result = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var (name, value) in nested)
        {
            if (value is string s) result[name] = s;
        }
        return result.Count == 0 ? null : result;
    }
}
