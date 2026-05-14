#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Events;

namespace MMP.Herald.Filters.Projection;

/// <summary>
/// One projection-filter rule. Tests a single property on the event
/// against a configured comparison and returns whether the event
/// passes. A rule's <see cref="Allow(LogEvent)"/> returns <c>true</c>
/// when the event satisfies the rule (the event flows through) and
/// <c>false</c> when the event should be dropped.
///
/// <para>The wire format is intentionally narrow:
/// <c>property:operator:value</c>. Three colon-separated fields,
/// nothing fancy. Operators: <c>eq</c>, <c>ne</c>, <c>contains</c>,
/// <c>startswith</c>, <c>endswith</c>, <c>exists</c>, <c>missing</c>,
/// <c>gt</c>, <c>lt</c>, <c>gte</c>, <c>lte</c>. Numeric operators
/// parse both sides as <see cref="double"/>.</para>
///
/// <para>This is the per-pipeline runtime that
/// <see cref="MMP.Herald.Quick.QuickLogBuilder.WithProjectionFilter"/>
/// reserves API surface for. The per-tenant overlay (where a tenant's
/// rule for the same property name overrides a global rule) is a
/// layer above this; that lands in a future iteration. For now,
/// rules apply per-pipeline only.</para>
/// </summary>
public sealed class ProjectionFilterRule
{
    public string Property { get; }
    public ProjectionFilterOperator Op { get; }
    public string? StringValue { get; }
    public double NumericValue { get; }

    private ProjectionFilterRule(string property, ProjectionFilterOperator op, string? stringValue, double numericValue)
    {
        Property = property;
        Op = op;
        StringValue = stringValue;
        NumericValue = numericValue;
    }

    /// <summary>
    /// Parse a rule from its wire form (<c>property:operator:value</c>).
    /// Throws <see cref="ArgumentException"/> with an actionable message
    /// when the rule cannot be parsed; the operator sees the failure
    /// at registration time, not at the first event.
    /// </summary>
    public static ProjectionFilterRule Parse(string property, string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(raw);

        // Some operators (exists / missing) take no value; allow the
        // bare-operator form. Otherwise the rule is operator:value.
        var firstColon = raw.IndexOf(':');
        var opStr = firstColon < 0 ? raw : raw.Substring(0, firstColon);
        var valueStr = firstColon < 0 ? null : raw.Substring(firstColon + 1);

        var op = opStr.Trim().ToLowerInvariant() switch
        {
            "eq"         => ProjectionFilterOperator.Equal,
            "ne"         => ProjectionFilterOperator.NotEqual,
            "contains"   => ProjectionFilterOperator.Contains,
            "startswith" => ProjectionFilterOperator.StartsWith,
            "endswith"   => ProjectionFilterOperator.EndsWith,
            "exists"     => ProjectionFilterOperator.Exists,
            "missing"    => ProjectionFilterOperator.Missing,
            "gt"         => ProjectionFilterOperator.GreaterThan,
            "lt"         => ProjectionFilterOperator.LessThan,
            "gte"        => ProjectionFilterOperator.GreaterOrEqual,
            "lte"        => ProjectionFilterOperator.LessOrEqual,
            _ => throw new ArgumentException(
                $"Unknown projection-filter operator '{opStr}'. " +
                "Expected one of: eq, ne, contains, startswith, endswith, exists, missing, gt, lt, gte, lte.",
                nameof(raw))
        };

        // exists / missing don't carry a value.
        if (op is ProjectionFilterOperator.Exists or ProjectionFilterOperator.Missing)
        {
            return new ProjectionFilterRule(property, op, null, 0);
        }

        if (valueStr is null)
            throw new ArgumentException($"Operator '{opStr}' requires a value (form: '{opStr}:value').", nameof(raw));

        // Numeric operators parse both sides as double.
        if (op is ProjectionFilterOperator.GreaterThan
                or ProjectionFilterOperator.LessThan
                or ProjectionFilterOperator.GreaterOrEqual
                or ProjectionFilterOperator.LessOrEqual)
        {
            if (!double.TryParse(valueStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
                throw new ArgumentException($"Numeric operator '{opStr}' requires a parseable double; got '{valueStr}'.", nameof(raw));
            return new ProjectionFilterRule(property, op, null, num);
        }

        return new ProjectionFilterRule(property, op, valueStr, 0);
    }

    /// <summary>
    /// Evaluate the rule against an event. Returns <c>true</c> when the
    /// event satisfies the rule (passes through), <c>false</c> when
    /// the event should be dropped.
    /// </summary>
    public bool Allow(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var (found, value) = ReadProperty(logEvent, Property);
        return Op switch
        {
            ProjectionFilterOperator.Exists  => found,
            ProjectionFilterOperator.Missing => !found,
            _ when !found => false,
            ProjectionFilterOperator.Equal      => string.Equals(ValueToString(value), StringValue, StringComparison.Ordinal),
            ProjectionFilterOperator.NotEqual   => !string.Equals(ValueToString(value), StringValue, StringComparison.Ordinal),
            ProjectionFilterOperator.Contains   => ValueToString(value).Contains(StringValue ?? "", StringComparison.Ordinal),
            ProjectionFilterOperator.StartsWith => ValueToString(value).StartsWith(StringValue ?? "", StringComparison.Ordinal),
            ProjectionFilterOperator.EndsWith   => ValueToString(value).EndsWith(StringValue ?? "", StringComparison.Ordinal),
            ProjectionFilterOperator.GreaterThan    => TryDouble(value, out var d) && d >  NumericValue,
            ProjectionFilterOperator.LessThan       => TryDouble(value, out var d) && d <  NumericValue,
            ProjectionFilterOperator.GreaterOrEqual => TryDouble(value, out var d) && d >= NumericValue,
            ProjectionFilterOperator.LessOrEqual    => TryDouble(value, out var d) && d <= NumericValue,
            _ => false
        };
    }

    /// <summary>
    /// Compose a list of rules into a single predicate that returns
    /// <c>true</c> only when ALL rules pass (logical AND). Operators
    /// who want OR semantics combine their rules into one with that
    /// shape upstream — keeping the engine narrow keeps the cognitive
    /// complexity at the call site low.
    /// </summary>
    public static Func<LogEvent, bool> Combine(IReadOnlyList<ProjectionFilterRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count == 0) return _ => true;
        return logEvent =>
        {
            for (var i = 0; i < rules.Count; i++)
            {
                if (!rules[i].Allow(logEvent)) return false;
            }
            return true;
        };
    }

    private static (bool Found, object? Value) ReadProperty(LogEvent logEvent, string name)
    {
        if (logEvent.Properties is not null)
        {
            foreach (var p in logEvent.Properties)
            {
                if (string.Equals(p.Name, name, StringComparison.Ordinal))
                    return (true, p.Value);
            }
        }
        if (logEvent.Context is not null && logEvent.Context.TryGetValue(name, out var ctxValue))
            return (true, ctxValue);
        return (false, null);
    }

    private static string ValueToString(object? value) => value?.ToString() ?? string.Empty;

    private static bool TryDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d: result = d; return true;
            case float f:  result = f; return true;
            case int i:    result = i; return true;
            case long l:   result = l; return true;
            case decimal m: result = (double)m; return true;
            default:
                return double.TryParse(value?.ToString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out result);
        }
    }
}

public enum ProjectionFilterOperator
{
    Equal,
    NotEqual,
    Contains,
    StartsWith,
    EndsWith,
    Exists,
    Missing,
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
}
