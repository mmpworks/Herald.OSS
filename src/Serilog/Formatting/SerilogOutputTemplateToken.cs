#nullable enable

namespace MMP.Herald.Serilog.Formatting;

/// <summary>A token produced by parsing a Serilog output-template string.</summary>
public abstract record SerilogOutputTemplateToken;

/// <summary>
/// Literal text segment, including <c>{{</c> / <c>}}</c> unescaped to <c>{</c> / <c>}</c>.
/// </summary>
public sealed record TextToken(string Text) : SerilogOutputTemplateToken;

/// <summary>
/// A property hole of the form <c>{Name,Alignment:Format}</c>.
/// </summary>
/// <param name="Name">Property name as written between the braces.</param>
/// <param name="Alignment">
/// Padding width: 0 = none, positive = right-align (left-pad), negative = left-align (right-pad).
/// </param>
/// <param name="Format">
/// Format specifier following the first <c>:</c> after the name/alignment section,
/// or <c>null</c> when absent. Subsequent colons inside the format string (e.g. <c>HH:mm:ss</c>)
/// are part of the value and are NOT treated as delimiters.
/// </param>
public sealed record HoleToken(string Name, int Alignment, string? Format) : SerilogOutputTemplateToken;
