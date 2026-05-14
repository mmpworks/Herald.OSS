#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Templating;

/// <summary>
/// Parsed message template consisting of ordered tokens.
/// </summary>
public sealed record MessageTemplate(
    string Raw,
    IReadOnlyList<MessageTemplateToken> Tokens);