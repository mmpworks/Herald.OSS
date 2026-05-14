#nullable enable

namespace MMP.Herald.Configuration;

/// <summary>
/// Controls retry behavior for a sink or decorator.
/// </summary>
public sealed record RetryPolicy(
    bool IsEnabled,
    int MaxAttempts,
    int DelayMs)
{
    public static RetryPolicy Disabled { get; } = new(
        IsEnabled: false,
        MaxAttempts: 1,
        DelayMs: 250);
}