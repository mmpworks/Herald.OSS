// Lines 1-16
#nullable enable

namespace MMP.Herald.Configuration.Runtime;
/// <summary>
/// Runtime alias definition normalized from transport config.
/// </summary>
public sealed record LoggingRuntimeAliasDefinition(
    string Key,
    string TransformerKind,
    string? BasedOnAlias = null);