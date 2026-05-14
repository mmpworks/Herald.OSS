#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Configuration.Json;

/// <summary>
/// JSON-facing configuration for the kernel-aware
/// <see cref="MMP.Herald.Pipeline.Kernel.FastPathDynamicLevel"/>. Carries
/// the initial level the LogLevelSwitch is configured with, plus an
/// optional per-category override map keyed by category value.
///
/// <para>
/// Reload semantics: the JSON's <see cref="InitialLevel"/> reinstalls the
/// switch fresh on every reload. Runtime mutations made via
/// <c>levelSwitch.MinimumLevel = ...</c> between reloads are not
/// preserved — that's a deliberate "JSON is the source of truth" choice
/// matching the legacy DynamicLevelPolicy reload behaviour. The same
/// applies to <see cref="Categories"/>: the dictionary serialised at
/// build time wins over any runtime additions made through the live
/// <see cref="MMP.Herald.Levels.CategoryLevelSwitchMap"/>.
/// </para>
/// </summary>
public sealed record JsonFastPathDynamicLevelConfig(
    string InitialLevel,
    IReadOnlyDictionary<string, string>? Categories = null);
