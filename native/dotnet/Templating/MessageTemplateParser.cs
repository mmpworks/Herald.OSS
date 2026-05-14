#nullable enable

using System;
using System.Collections.Generic;

namespace MMP.Herald.Templating;

/// <summary>
/// Parses and renders structured message templates.
/// Parsing is delegated to the configured <see cref="ITemplateParseStrategy"/>;
/// rendering is delegated to <see cref="MessageTemplateRenderer"/>.
///
/// The strategy is chosen once at construction and dispatched through the
/// interface on every Parse call — no per-call branching on cache mode.
/// Named presets live on <see cref="TemplateParseStrategies"/>.
///
/// Supported template syntax:
/// - <c>{Name}</c>, <c>{@Name}</c>, <c>{$Name}</c>, <c>{Name:000}</c>
/// - escaped braces: <c>{{</c> and <c>}}</c>
/// </summary>
public sealed class MessageTemplateParser
{
    private readonly MessageTemplateRenderer _renderer;
    private readonly ITemplateParseStrategy _strategy;

    public MessageTemplateParser(
        DestructuringPolicyRegistry? destructuringPolicies = null,
        ITemplateParseStrategy? parseStrategy = null) {
        _renderer = new MessageTemplateRenderer(destructuringPolicies);
        _strategy = parseStrategy ?? TemplateParseStrategies.LiteralFirst();
    }

    // -- Parsing --

    public MessageTemplate Parse(string template) {
        ArgumentNullException.ThrowIfNull(template);
        return _strategy.Parse(template);
    }

    // -- Rendering (delegates to MessageTemplateRenderer) --

    public RenderedMessage Render(
        string template,
        IReadOnlyList<LogProperty> properties) {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(properties);

        return _renderer.Render(_strategy.Parse(template), properties);
    }

    public RenderedMessage Render(
        MessageTemplate template,
        IReadOnlyList<LogProperty> properties) =>
        _renderer.Render(template, properties);
}
