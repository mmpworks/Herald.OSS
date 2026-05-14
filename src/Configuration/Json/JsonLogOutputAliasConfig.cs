// Lines 1-16
#nullable enable

namespace MMP.Herald.Configuration.Json;
/// <summary>
/// JSON-facing output alias definition.
/// TransformerKind is adapter-specific and interpreted by the host layer.
/// </summary>
public sealed record JsonLogOutputAliasConfig(
    string Key,
    string TransformerKind,
    string? BasedOnAlias = null);