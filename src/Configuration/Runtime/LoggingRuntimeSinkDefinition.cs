#nullable enable

using System.Collections.Generic;
using MMP.Herald.Levels;

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime sink definition with normalized string values.
///
/// <para>
/// <b>Label.</b> Used by the <c>ExternalSourceRegistrar</c> to identify
/// sinks for external-injection registrations. Defaults to a random
/// 32-hex-char string when null or empty so every sink in a pipeline
/// has a unique label even if the operator didn't pre-define one.
/// Distinct from <see cref="Name"/> — the name is the config-time
/// identifier (predictable, may collide across pipelines); the label
/// is the runtime identifier the security registrar consults.
/// </para>
///
/// <para>
/// <b>RunState + loopback flags.</b> Together these gate how an event
/// reaches the actual sink implementation:
/// <list type="bullet">
///   <item><see cref="RunState"/> = <c>Disabled</c> short-circuits
///         immediately — the event is dropped before any other check
///         runs.</item>
///   <item><see cref="RunState"/> = <c>Live</c> sends to the real
///         destination. Additionally, when
///         <see cref="TeeLiveToFile"/> is set AND the pipeline
///         carries a <c>TestLoopbackLogDir</c>, a copy is teed to that
///         file. Same pairing for <see cref="TeeLiveToUrl"/>
///         and the pipeline's <c>TestLoopbackUrl</c>. The booleans are
///         the per-sink opt-in for live teeing — without them, live
///         is silent on the loopbacks even if the pipeline defined
///         them.</item>
///   <item><see cref="RunState"/> = <c>Test</c> suppresses the real
///         send. The two live booleans are <i>not</i> consulted in
///         test mode — every loopback the pipeline defined receives
///         the event. That keeps the dashboard's "let me peek at the
///         traffic this sink would generate" workflow simple: flip
///         to test, watch whichever loopback is configured.</item>
/// </list>
/// <see cref="TestOutFileSuffix"/> is the per-sink string that
/// disambiguates loopback files when several sinks share the same
/// pipeline-level <c>TestLoopbackLogDir</c>. The resolved file path is
/// <c>{TestLoopbackLogDir}/{TestOutFileSuffix}-{Name}.{ext}</c>, where
/// <c>{ext}</c> is <c>ndjson</c> when the pipeline's
/// <c>LoopbackUseNdjson</c> flag is true (default) and <c>log</c>
/// when it is false. The extension follows the format so a directory
/// listing immediately tells the operator whether the files are
/// machine-parseable or template-rendered.
/// </para>
/// </summary>
public sealed record LoggingRuntimeSinkDefinition(
    string Name,
    string Kind,
    string? Path = null,
    string? Alias = null,
    string? Uri = null,
    string? Host = null,
    int? Port = null,
    LoggingRuntimeRetryPolicy? RetryPolicy = null,
    LoggingRuntimeFileRollingPolicy? RollingPolicy = null,
    LogLevel? MinLevel = null,
    string? OutputTemplate = null,
    bool IsAudit = false,
    bool EnableMetrics = false,
    string? Label = null,
    SinkRunState RunState = SinkRunState.Live,
    string? TestOutFileSuffix = null,
    bool TeeLiveToFile = false,
    bool TeeLiveToUrl = false,
    IReadOnlyDictionary<string, object?>? Properties = null);
// v2 sink-config bag: every key declared in the sink's mmpform
// __properties block lands here, with values merged from the JSON
// config and defaults filled in. Sinks read from Properties at
// CreateSink time and copy the values they need into concrete fields
// on the writer; the per-event hot path never touches the dictionary,
// so the bag stays a build-time concern. Null when the sink has not
// migrated to v2 yet — providers must tolerate that during the
// transition (Path / RollingPolicy / etc. above remain the legacy
// source of truth until each sink moves over).