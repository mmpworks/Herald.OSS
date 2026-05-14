#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for the kernel-aware
/// <see cref="MMP.Herald.Pipeline.Kernel.FastPathRedactor"/>. Travels
/// through the JSON round-trip so a <c>Reload(json)</c> reconstructs the
/// same redaction rules without the host having to call
/// <c>WithFastRedaction</c> again.
///
/// <para>
/// Scope mirrors the runtime path: exact-name rules with
/// Remove / Mask / Hash modes only. Pattern rules, event-action rules,
/// and value-pattern conditions live on <c>EventProcessors</c> instead
/// (their JSON shape is the legacy
/// <see cref="MMP.Herald.Addons.Compliance.RedactionRuleParser"/> grammar).
/// </para>
/// </summary>
public sealed record JsonFastPathRedactionConfig(
    IReadOnlyList<JsonFastPathRedactionRule> Rules);

/// <summary>One rule in <see cref="JsonFastPathRedactionConfig"/>.</summary>
public sealed record JsonFastPathRedactionRule(
    string PropertyName,
    /// <summary>"remove" | "mask" | "hash" — case-insensitive on read.</summary>
    string Mode,
    char MaskChar = '*',
    int VisibleChars = 0);
