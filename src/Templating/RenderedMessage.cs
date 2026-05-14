#nullable enable

using System.Collections.Generic;

namespace MMP.Herald.Templating;

/// <summary>
/// Result of rendering a message template with a set of properties.
/// </summary>
public sealed record RenderedMessage(
    string Template,
    string Message,
    IReadOnlyList<LogProperty> Properties);