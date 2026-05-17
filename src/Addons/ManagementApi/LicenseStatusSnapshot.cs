// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Transport-shaped snapshot of the host's license state, returned by
/// <see cref="HeraldManagementApi.LicenseStatusProvider"/>. The
/// Dashboard reads license state through the OSS facade without
/// importing any Server-internal type — strings and a
/// <see cref="DateTimeOffset"/> only, no edition enums, no v1
/// globals, no commercial-tier knowledge.
///
/// <para>
/// <b>Why so small.</b> Pulling a richer status type into OSS would
/// drag v1 license globals along with it. The Dashboard only needs
/// enough to render a status badge and an expiry / failure
/// explanation. Hosts that want more detail extend the wire shape on
/// their side; OSS doesn't grow surface for it.
/// </para>
///
/// <para>
/// <b>Fields.</b>
/// <list type="bullet">
///   <item><c>Kind</c> — short tag the renderer switches on:
///   <c>"valid"</c>, <c>"missing"</c>, <c>"expired"</c>,
///   <c>"wrong-product"</c>, <c>"trial"</c>, etc. Strings, not an
///   enum, so a host can ship a new state without an OSS bump.</item>
///   <item><c>Edition</c> — edition name as a string. The OSS layer
///   doesn't know about commercial editions, so the host stringifies
///   whatever edition concept it owns.</item>
///   <item><c>ExpiresAt</c> — license expiry. Null when not
///   applicable (perpetual license, missing license).</item>
///   <item><c>Reason</c> — operator-readable detail. Null when the
///   <c>Kind</c> is self-explanatory.</item>
/// </list>
/// </para>
/// </summary>
/// <param name="Kind">Short tag the renderer switches on.</param>
/// <param name="Edition">Edition name as a string; null when not applicable.</param>
/// <param name="ExpiresAt">License expiry; null when not applicable.</param>
/// <param name="Reason">Operator-readable detail; null when the kind is self-explanatory.</param>
public readonly record struct LicenseStatusSnapshot(
    string Kind,
    string? Edition,
    DateTimeOffset? ExpiresAt,
    string? Reason);
