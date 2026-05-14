#nullable enable

using System;

namespace MMP.Herald.Configuration;
/// <summary>
/// Controls whether async logging is enabled and, if so, with what capacity.
/// </summary>
public sealed record AsyncLogPolicy(
    bool IsEnabled,
    int Capacity,
    string DropStrategy = MMP.Herald.Services.KnownDropStrategies.DropWrite,
    bool DeferRendering = false)
{
    public static AsyncLogPolicy Disabled { get; } = new(
        IsEnabled: false,
        Capacity: 0,
        DropStrategy: MMP.Herald.Services.KnownDropStrategies.DropWrite);

    public static AsyncLogPolicy Enabled(int capacity, string dropStrategy = MMP.Herald.Services.KnownDropStrategies.DropWrite)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Async capacity must be greater than zero when async logging is enabled.");
        }

        return new AsyncLogPolicy(
            IsEnabled: true,
            Capacity: capacity,
            DropStrategy: dropStrategy);
    }
}