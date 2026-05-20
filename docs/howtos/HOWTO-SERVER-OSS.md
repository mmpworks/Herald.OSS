# Herald.ServerOSS — coming in v0.9.0

Herald.ServerOSS is a thin wrapper over Herald.RestApi.Host that exposes
the OSS-grade implementations of the Herald.RestApi.Contracts interfaces.
It ships as a separate NuGet package plus a `dotnet new herald-server-oss`
template alongside Herald.OSS.

**Coming in v0.9.0.** Track progress via GitHub releases.

## In the meantime

The closest working surface today is the
[Herald.SampleApps.HttpApi sample](https://github.com/mmpworks/Herald/tree/stage-0-phase-2c-package-bump/Modules/Server/samples/Herald.SampleApps.HttpApi),
which embeds Herald.OSS into an ASP.NET Core HTTP API and demonstrates
the "latch onto an existing host" pattern — the application's own
endpoints sit alongside Herald's management API on the same port,
served from the same process. Live-log capture is wired up via SSE.

That sample covers the surface Herald.ServerOSS will package as a
turn-key host: management API, capture pipeline, and the endpoints
needed to drive a dashboard. When v0.9.0 ships you will be able to
replace the sample's hand-wired `app.MapHerald(...)` with the
Herald.ServerOSS package and a one-line `builder.AddHeraldServerOSS(...)`.

## What ships in v0.9.0

- `Herald.ServerOSS` NuGet package (Apache 2.0) — the host wrapper.
- `dotnet new herald-server-oss` template — scaffolds a working host
  project with management API, capture pipeline, and configuration.
- Conformance against the `Herald.RestApi.Contracts.Conformance` suite.

See [`HOWTO-QUICKSTART.md`](HOWTO-QUICKSTART.md) for the current
Herald.OSS install path.
