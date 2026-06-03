# Ref1.Worker — Worker / Console (find-replace)

**What it does.** The bread-and-butter Serilog app: static `Log.Information/Warning/Error`,
Console + File sinks, configured from `appsettings.json` via `ReadFrom.Configuration`. Logs a
startup line, processes three orders, warns on queue depth, errors on a failed endpoint.

**Vehicle.** Find-replace (`MMP.Herald.Serilog` 0.12.5). Config-driven apps are not
zero-source-change-eligible — there is no Layer-2 `ReadFrom.Configuration` bridge — so the
realistic path is the namespace swap.

**What migration touched.**
- `Program.cs`: `using Serilog;` → `using MMP.Herald.Serilog;`, plus one **added**
  `using Herald.OSS.Serilog.Settings;` (that is where `ReadFrom.Configuration` lives).
- `Ref1.Worker.csproj`: dropped the `Serilog.Sinks.*` + `Serilog.Settings.Configuration`
  packages; added `MMP.Herald.Serilog` + `Herald.OSS.Serilog.Settings`.

**Before/after worth showing.** The two-line `using` block side by side, and the csproj
package list shrinking from five Serilog packages to two Herald packages. Output is
semantically identical — same messages, levels, and structured properties.

**Gotcha for the page.** `ReadFrom.Configuration` is not a single-line swap: it needs the
added `using Herald.OSS.Serilog.Settings;`. State that plainly.
