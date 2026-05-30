# P5 build.sh wiring — action for Max

Two new projects need adding to the umbrella build:
- `compat/Herald.OSS.Serilog.Settings/Herald.OSS.Serilog.Settings.csproj`
- `compat/Herald.OSS.Serilog.Settings.Tests/Herald.OSS.Serilog.Settings.Tests.csproj`

Both are net9/net10 only (they override the repo-default TFM). 
Neither should be included in any net8 build target.
The test project is not packable (IsPackable=false).

NuGet packaging (when ready): `Herald.OSS.Serilog.Settings` is the Apache-2.0 standalone
package. Do not package the .Tests project. Set version from the umbrella's Herald version
convention.
