# Ref2.WebApi — ASP.NET Core Minimal API (find-replace)

**What it does.** A minimal API that wires the full Serilog ASP.NET surface: `UseSerilog`
host configuration, `UseSerilogRequestLogging` middleware, `appsettings.json`, and one
`WithProperty` enricher. The sample starts Kestrel on a loopback port, issues one self-request
so the request-logging middleware fires, then shuts down.

**Vehicle.** Find-replace (`MMP.Herald.Serilog.AspNetCore` 0.12.5).

**What migration touched.**
- `Program.cs`: `using Serilog;` → `using MMP.Herald.Serilog;` + added
  `using Herald.OSS.Serilog.Settings;`. Two small API-parity edits: `CreateBootstrapLogger()`
  → `CreateLogger()` and `CloseAndFlushAsync()` → `CloseAndFlush()`.
- `Ref2.WebApi.csproj`: `Serilog.AspNetCore` + `Serilog.Sinks.Console` → `MMP.Herald.Serilog.AspNetCore`
  + `Herald.OSS.Serilog.Settings`.

**Before/after worth showing.** The host-wiring block (`builder.Host.UseSerilog(...)`) is
byte-for-byte the same; `app.UseSerilogRequestLogging()` is unchanged. The migrated run shows
`MMP.Herald.Serilog.AspNetCore.RequestLoggingMiddleware - HTTP GET / responded 200` — the
middleware genuinely fired.

**Gotchas for the page.** `CreateBootstrapLogger()` and `CloseAndFlushAsync()` are not in the
Herald surface yet — name the one-word substitutions. ASP.NET wiring otherwise carries over
because the extensions live in the framework namespaces the app already imports.
