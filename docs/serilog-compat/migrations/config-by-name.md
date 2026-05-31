---
gap-id: config-by-name
serilog-surface: sink/enricher by name in appsettings.json ("Using" / "Name")
herald-status: carries-over (LoggerSinkRegistry.RegisterSink — one call, no parser fork)
population-rank: high
regression-test-id: G-SINK-WALL.1
---

<!-- Heather T-H2: STANDALONE companion. The S-NEW-1 day-one-friction gap — lands on
     the highest-value customer (in-house sink wired by name). The one call that avoids
     forking the parser. -->

# Migrating Sink/Enricher-by-Name in appsettings.json

## What you have in Serilog

A large share of production Serilog apps configure sinks by name in `appsettings.json`, including in-house company sinks:

```json
{
  "Serilog": {
    "Using": ["MyCompany.Logging"],
    "WriteTo": [
      {
        "Name": "MyCompanySink",
        "Args": { "connectionString": "..." }
      }
    ],
    "Enrich": ["MyCompanyEnricher"]
  }
}
```

Serilog resolves `"MyCompanySink"` by scanning the assembly listed in `"Using"` for a `WriteTo.MyCompanySink(...)` extension method. Herald's settings parser knows only the built-in Herald sink set — it cannot scan arbitrary assemblies for extension methods.

Without registration, a `"MyCompanySink"` entry hits an unresolved name and throws. There is no path to make it resolve by adding an assembly to `"Using"`. The only way to avoid forking the parser is to register the name.

## What changes

One call, placed before the parser runs. `LoggerSinkRegistry.Default.RegisterSink(name, factory)` registers your in-house sink by name. The parser consults the registry before it fails.

<!-- FILL AFTER P5: confirm exact RegisterSink signature from the shipped settings project. -->

The pattern:

```csharp
// In your startup code, before building the logger from configuration
LoggerSinkRegistry.Default.RegisterSink(
    "MyCompanySink",
    (loggerConfiguration, args) =>
    {
        var connectionString = args["connectionString"]?.ToString();
        loggerConfiguration.WriteTo.Sink(new MyCompanySink(connectionString));
        return loggerConfiguration;
    }
);

// For enrichers
LoggerEnricherRegistry.Default.RegisterEnricher(
    "MyCompanyEnricher",
    (loggerConfiguration, args) =>
    {
        loggerConfiguration.Enrich.With(new MyCompanyEnricher());
        return loggerConfiguration;
    }
);
```

After this call, the parser resolves `"MyCompanySink"` and `"MyCompanyEnricher"` from the registry. Your `appsettings.json` requires no changes.

## Step-by-step

1. In your startup code (before `ReadFrom.Configuration(...)` runs), add the registration call for each in-house sink and enricher your `appsettings.json` references by name.

2. Reference the Layer-1 or Layer-2 settings package:
   ```xml
   <PackageReference Include="MMP.Herald.Serilog.Settings.Configuration" Version="x.y.z" />
   ```

3. Rebuild.

4. Start the application. If the sink resolves, you will see it active in the pipeline. If it fails, you will get a `SinkResolutionException` (see below).

## Unresolved names fail loud

An unregistered name throws `SinkResolutionException` at configuration time — before your application processes any events. The exception message includes:

- The sink name that could not be resolved
- The set of names that are registered
- A pointer to this documentation

This is the same loud-fail family as the Seq identity wall (G-SINK-WALL.1). A silent no-op is not an option — a sink that appears to configure but does not run is worse than one that fails fast.

## Verify

After registration, start the application and confirm:

- No `SinkResolutionException` fires at startup.
- Log events flow to your in-house sink as expected.
- The `Args` dictionary in the factory lambda contains the keys from your `appsettings.json` entry's `"Args"` object.

If you have multiple environments with different `appsettings.{env}.json` files that reference the same sink name, confirm the registration is in place before any environment's configuration is read — registrations are global and must be set up once at startup.

<!-- Note (open decision #3 in P8 plan): if P0 Task 9 removed the alias map and old on-disk
     configs need a one-time migration shim, a "migrating old-key configs" note will be added
     here once Richard's P0 resolution is confirmed. Check P0 Task 9 status before deploying
     to production if your appsettings.json used Herald-specific key names pre-compat. -->
