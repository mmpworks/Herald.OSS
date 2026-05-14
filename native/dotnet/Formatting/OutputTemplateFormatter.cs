#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MMP.Herald.Events;
using MMP.Herald.Levels;
using MMP.Herald.Pooling;
using MMP.Herald.Templating;

namespace MMP.Herald.Formatting;

/// <summary>
/// Formats log events using a user-defined output template string.
/// Supported tokens: {Timestamp}, {Timestamp:format}, {Level}, {LevelKey}, {Category},
/// {Message}, {Properties}, {Context}, {Exception}, {NewLine}.
/// Escaped braces {{ and }} render as literals. Unrecognized tokens render verbatim.
///
/// Token dispatch uses a dictionary of delegates compiled once at construction,
/// avoiding per-event switch branching and ToLowerInvariant() allocations.
/// </summary>
public sealed class OutputTemplateFormatter : ILogFormatter
{
    private readonly ILogLevelRegistry _levelRegistry;
    private readonly IReadOnlyList<OutputTemplateToken> _tokens;

    // Dispatch table: token name (lowercase) -> append action.
    // Built once at construction. Eliminates the per-event switch + ToLowerInvariant().
    private static readonly Dictionary<string, Action<StringBuilder, OutputTemplateToken.Token, LogEvent, RegisteredLogLevel>> TokenHandlers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [OutputTemplateTokenNames.Timestamp] = static (sb, token, evt, _) => {
                var format = string.IsNullOrWhiteSpace(token.Format) ? "O" : token.Format;
                sb.Append(evt.TimeUtc.ToString(format, CultureInfo.InvariantCulture));
            },
            [OutputTemplateTokenNames.Level] = static (sb, _, _, reg) => sb.Append(reg.Level.DisplayName),
            [OutputTemplateTokenNames.LevelKey] = static (sb, _, _, reg) => sb.Append(reg.Level.Key),
            [OutputTemplateTokenNames.Category] = static (sb, _, evt, _) => sb.Append(evt.Category.Value),
            [OutputTemplateTokenNames.Message] = static (sb, _, evt, _) => sb.Append(evt.Message),
            [OutputTemplateTokenNames.Properties] = static (sb, _, evt, _) => AppendProperties(sb, evt.Properties),
            [OutputTemplateTokenNames.Context] = static (sb, _, evt, _) => AppendContext(sb, evt.Context),
            [OutputTemplateTokenNames.Exception] = static (sb, _, evt, _) => AppendException(sb, evt.Context),
            [OutputTemplateTokenNames.NewLine] = static (sb, _, _, _) => sb.Append(Environment.NewLine),
        };

    public OutputTemplateFormatter(ILogLevelRegistry levelRegistry, string outputTemplate) {
        ArgumentNullException.ThrowIfNull(levelRegistry);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputTemplate);
        _levelRegistry = levelRegistry;
        _tokens = Parse(outputTemplate);
    }

    public string Format(LogEvent logEvent) {
        ArgumentNullException.ThrowIfNull(logEvent);

        var registeredLevel = _levelRegistry.GetRegisteredLevel(logEvent.Level);
        var builder = StringBuilderPool.Rent();

        foreach (var token in _tokens)
        {
            switch (token)
            {
                case OutputTemplateToken.Text text:
                    builder.Append(text.Value);
                    break;

                case OutputTemplateToken.Token t:
                    AppendToken(builder, t, logEvent, registeredLevel);
                    break;
            }
        }

        return StringBuilderPool.ReturnAndGetString(builder);
    }

    private static void AppendToken(
        StringBuilder builder,
        OutputTemplateToken.Token token,
        LogEvent logEvent,
        RegisteredLogLevel registeredLevel) {
        if (TokenHandlers.TryGetValue(token.Name, out var handler))
        {
            handler(builder, token, logEvent, registeredLevel);
            return;
        }

        // Unrecognized token: preserve raw form so templates survive format changes
        builder.Append('{');
        builder.Append(token.Name);
        if (!string.IsNullOrWhiteSpace(token.Format))
        {
            builder.Append(':');
            builder.Append(token.Format);
        }
        builder.Append('}');
    }

    private static void AppendProperties(
        StringBuilder builder,
        IReadOnlyList<LogProperty> properties) {
        var first = true;

        foreach (var property in properties)
        {
            if (!first) builder.Append(", ");
            builder.Append(property.Name);
            builder.Append('=');
            builder.Append(property.ResolvedValue?.ToString() ?? "null");
            first = false;
        }
    }

    private static void AppendContext(
        StringBuilder builder,
        IReadOnlyDictionary<string, object?> context) {
        var first = true;

        using var sorted = SortedContextBuffer.Create(context);
        foreach (var pair in sorted.AsSpan())
        {
            if (pair.Value is Exception) continue;

            if (!first) builder.Append(", ");
            builder.Append(pair.Key);
            builder.Append('=');
            builder.Append(pair.Value?.ToString() ?? "null");
            first = false;
        }
    }

    private static void AppendException(
        StringBuilder builder,
        IReadOnlyDictionary<string, object?> context) {
        if (!context.TryGetValue(Services.LogContextKeys.Exception, out var raw) || raw is not Exception exception)
        {
            return;
        }

        builder.Append(exception.GetType().FullName ?? exception.GetType().Name);
        builder.Append(": ");
        builder.Append(exception.Message);

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.Append(Environment.NewLine);
            builder.Append(exception.StackTrace);
        }
    }

    private static IReadOnlyList<OutputTemplateToken> Parse(string template) {
        var tokens = new List<OutputTemplateToken>();
        var textBuffer = new StringBuilder();
        var index = 0;

        while (index < template.Length)
        {
            var current = template[index];

            if (current == '{')
            {
                if (index + 1 < template.Length && template[index + 1] == '{')
                {
                    textBuffer.Append('{');
                    index += 2;
                    continue;
                }

                FlushText(tokens, textBuffer);

                var end = template.IndexOf('}', index + 1);

                if (end < 0)
                {
                    textBuffer.Append(template[index..]);
                    break;
                }

                var content = template[(index + 1)..end].Trim();
                var colonIndex = content.IndexOf(':');
                var name = colonIndex >= 0 ? content[..colonIndex].Trim() : content;
                var format = colonIndex >= 0 ? content[(colonIndex + 1)..].Trim() : null;

                tokens.Add(new OutputTemplateToken.Token(
                    name,
                    string.IsNullOrWhiteSpace(format) ? null : format));

                index = end + 1;
                continue;
            }

            if (current == '}' && index + 1 < template.Length && template[index + 1] == '}')
            {
                textBuffer.Append('}');
                index += 2;
                continue;
            }

            textBuffer.Append(current);
            index += 1;
        }

        FlushText(tokens, textBuffer);
        return tokens;
    }

    private static void FlushText(List<OutputTemplateToken> tokens, StringBuilder buffer) {
        if (buffer.Length == 0) return;
        tokens.Add(new OutputTemplateToken.Text(buffer.ToString()));
        buffer.Clear();
    }
}
