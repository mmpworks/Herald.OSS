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

## P6 — MMP.Herald.Serilog.AspNetCore

Two more projects need wiring into the umbrella build:
- `src/Serilog.AspNetCore/MMP.Herald.Serilog.AspNetCore.csproj`  (uses FrameworkReference Include="Microsoft.AspNetCore.App")
- `tests/AspNetCore/Herald.OSS.Serilog.AspNetCore.Tests.csproj`

Both are net9/net10 only. The product assembly uses FrameworkReference (not PackageReference) for ASP.NET Core types — do not try to pin Microsoft.AspNetCore.Http.Abstractions as a version-pinned PackageReference.
