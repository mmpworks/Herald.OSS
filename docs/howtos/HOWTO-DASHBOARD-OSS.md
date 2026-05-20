# Herald.DashboardOSS — coming in v0.9.0

Herald.DashboardOSS is the OSS distribution of the Vue 3 management SPA.
It talks to any Herald server (OSS or paid) via the
Herald.RestApi.Contracts surface and renders live pipeline state, sink
configuration, and event streams.

**Coming in v0.9.0.** Track progress via GitHub releases.

## What ships in v0.9.0

- `Herald.DashboardOSS` distribution — the prebuilt SPA plus a NuGet
  package that wires it into a Herald.ServerOSS host as static files.
- Live event stream over SSE.
- Pipeline configuration view (read-only in OSS; the paid editions
  layer write paths on top).
- Sink status, recent failures, and the diagnostics channel.

The dashboard is server-agnostic — it talks to the Herald.RestApi
surface, so any conformant host (OSS or paid) serves it the same way.

See [`HOWTO-SERVER-OSS.md`](HOWTO-SERVER-OSS.md) for the companion
host package and [`HOWTO-QUICKSTART.md`](HOWTO-QUICKSTART.md) for the
current Herald.OSS install path.
