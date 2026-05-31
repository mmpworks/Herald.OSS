// MMP.Herald.Serilog assembly-level attributes.
using System.Runtime.CompilerServices;

// Test assembly grant: allows Herald.OSS.Tests to access internal types
// (LogEventValueProjector, LogEvent constructor) for G-VM.* parity tests.
// Seam grant for P4 assembly: exposes DefaultValueFactory + HeraldPropertyValueFactory
// to the P4 plugin assembly (not yet named -- to be added when P4 ships).
[assembly: InternalsVisibleTo("Herald.OSS.Tests")]

// P6 ASP.NET Core wiring layer: MMP.Herald.Serilog.AspNetCore needs access to
// LoggerConfiguration.BuildStructuredLogger() so UseSerilog(lambda) can hand
// a configured StructuredLogger to the MEL provider without coupling through the
// public ILogger surface or exposing a StructuredLogger on the public API.
[assembly: InternalsVisibleTo("MMP.Herald.Serilog.AspNetCore")]

// P6 test project: needs the same internal access to construct and verify
// the UseSerilog() host hook without wrapping everything behind a public API.
[assembly: InternalsVisibleTo("Herald.OSS.Serilog.AspNetCore.Tests")]

// Settings-layer test project: the G1.3 delivery test reaches LoggerConfiguration.Builder
// to call SinkProviderSet.SetFallbackRegistry, injecting an isolated native-sink registry
// so the build-time provider instantiation can be asserted without touching the
// process-wide LogSinkProviderRegistry.Default.
[assembly: InternalsVisibleTo("Herald.OSS.Serilog.Settings.Tests")]
