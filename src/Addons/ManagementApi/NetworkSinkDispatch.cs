// Copyright (c) 2026 MMP LLC
// Licensed under the MIT License. See LICENSE in the project root.
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
    /// Sinks the dashboard configures with (uri, minLevel) — the most
    /// common shape. Each value is the builder method that takes
    /// <c>(string uri, string? minLevel)</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Action<QuickLogBuilder, string, string?>> UriSinks
        = new Dictionary<string, Action<QuickLogBuilder, string, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            [KnownSinkKinds.HttpJson]       = (b, uri, lvl) => b.WithHttpJsonSink(uri, lvl),
            [KnownSinkKinds.Elasticsearch]  = (b, uri, lvl) => b.WithElasticsearchSink(uri, lvl),
            [KnownSinkKinds.SlackWebhook]   = (b, uri, lvl) => b.WithSlackWebhookSink(uri, lvl),
            [KnownSinkKinds.GenericWebhook] = (b, uri, lvl) => b.WithWebhookSink(uri, lvl),
            [KnownSinkKinds.OtlpJson]       = (b, uri, lvl) => b.WithOtlpJsonSink(uri, lvl),
            [KnownSinkKinds.OtlpProtobuf]   = (b, uri, lvl) => b.WithOtlpProtobufSink(uri, lvl),
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
}
