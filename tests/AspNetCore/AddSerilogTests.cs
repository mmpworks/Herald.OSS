#nullable enable

// AddSerilog MEL provider registration tests — Task 2 of P6 ASP.NET Core wiring.
//
// Uses TestLogPipelineBuilder + InMemoryLogSink directly (MMP.Herald.Testing)
// because TestLoggers lives in Herald.OSS.Tests.csproj which is not referenced
// here — the typed wrapper would add a test-project dependency cycle.

using System;
using System.Linq;
using MMP.Herald.Levels;
using MMP.Herald.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Herald.OSS.Serilog.AspNetCore.Tests;

public sealed class AddSerilogTests
{
    // ---------------------------------------------------------------------------
    // Helper: build a capturing (StructuredLogger, InMemoryLogSink) pair
    // ---------------------------------------------------------------------------

    private static (MMP.Herald.Pipeline.StructuredLogger Logger, InMemoryLogSink Sink)
        CreateCapturing() =>
        new TestLogPipelineBuilder()
            .WithMinimumLevel(KnownLogLevels.Verbose)
            .Build();

    // ---------------------------------------------------------------------------
    // T-MEL-1: MEL ILogger<T> routes through Herald pipeline
    // ---------------------------------------------------------------------------

    [Fact]
    public void AddSerilog_routes_MEL_ILoggerOfT_through_Herald()
    {
        // Arrange
        var (heraldLogger, sink) = CreateCapturing();

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddSerilog(heraldLogger));
        using var sp = services.BuildServiceProvider();

        // Act
        sp.GetRequiredService<ILogger<AddSerilogTests>>()
            .LogInformation("Player {Name} joined", "Ada");

        // Assert
        var captured = sink.GetEvents();
        Assert.Single(captured);
        Assert.Equal("information", captured[0].Level.Key, StringComparer.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------------------
    // T-MEL-2: FM-5 — one call registers exactly one ILoggerProvider
    // ---------------------------------------------------------------------------

    [Fact]
    public void AddSerilog_registers_exactly_one_provider_not_two()
    {
        // Arrange
        var (heraldLogger, _) = CreateCapturing();

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders().AddSerilog(heraldLogger));

        // Count descriptors whose ServiceType is ILoggerProvider.
        // ClearProviders() removes the default console/debug providers, so
        // only our HeraldLoggerProvider descriptor should remain.
        var count = services.Count(d => d.ServiceType == typeof(ILoggerProvider));

        Assert.Equal(1, count);
    }

    // ---------------------------------------------------------------------------
    // T-MEL-3: FM-5 double-registration guard — two AddSerilog calls → one provider
    // ---------------------------------------------------------------------------

    [Fact]
    public void AddSerilog_twice_still_registers_only_one_provider()
    {
        // Arrange
        var (heraldLogger, _) = CreateCapturing();

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddSerilog(heraldLogger);  // first call
            b.AddSerilog(heraldLogger);  // second call — must be a no-op via TryAddEnumerable
        });

        var count = services.Count(d => d.ServiceType == typeof(ILoggerProvider));

        Assert.Equal(1, count);
    }

    // ---------------------------------------------------------------------------
    // T-MEL-4: Parameterless overload (ambient Log.Logger) skipped — requires
    // SerilogLoggerAdapter to expose the inner StructuredLogger. FM-5 advisory:
    // implement only after P1 facade exposes StructuredLogger publicly or
    // via an internal friend grant to this assembly.
    // ---------------------------------------------------------------------------

    [Fact(Skip = "Requires P1 Log.Logger to expose the inner StructuredLogger " +
                 "either publicly or via InternalsVisibleTo. Implement in a " +
                 "follow-up task once the facade contract is settled.")]
    public void AddSerilog_parameterless_reads_ambient_Log_Logger()
    {
        // TODO: set MMP.Herald.Serilog.Log.Logger = new SerilogLoggerAdapter(heraldLogger),
        // call b.AddSerilog() (parameterless), resolve ILogger<T>, log, assert captured.
        Assert.Fail("Not yet implemented.");
    }
}
