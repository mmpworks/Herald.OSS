# HRLDxxxx diagnostic codes

Herald.OSS's interceptor generator (P3) reserves the `HRLD0001..HRLD0099` range
for MSBuild-property validation. Each ID corresponds to one input the generator
reads at build time; the diagnostic message names the property, the invalid
value, and the suggested fix.

This range is separate from the older `HERALD0xx`/`HERALD04xx` family, which
covers analyzer rules (`HERALD001..HERALD006`, `HERALD007`, `HERALD0410`,
`HERALD0411`).

## V1 codes

| ID | Severity | Trigger | Suggested fix |
|----|----------|---------|---------------|
| `HRLD0001` | Error | `<HeraldInterceptorsEnabled>` MSBuild value is not `true` or `false`. | Use `true` (default — bake interceptors) or `false` (disable, runtime resolver for every call site). |
| `HRLD0002` | Error | `<HeraldStrictMode>` MSBuild value is not `true` or `false`. | Use `true` (escalate Herald analyzer warnings to errors) or `false` (default — leave them as warnings). |
| `HRLD0050` | Warning | Assembly's interceptor surface exceeds the soft threshold (5,000 call sites). | Group callers into hot-path facades or move to `[HeraldLog]` partial methods. The warning is operator-hint only — the build still succeeds. |

## Property reference

| MSBuild property | Default | Allowed values | What it controls |
|------------------|---------|----------------|------------------|
| `HeraldInterceptorsEnabled` | `true` | `true`, `false` | Master switch for the interceptor generator. When `false`, no `HeraldInterceptors.g.cs` or `HeraldBuildAssertion.g.cs` is emitted; every literal-template call site stays on the runtime resolver path. |
| `HeraldStrictMode` | `false` | `true`, `false` | Promotes Herald analyzer warnings (`HERALD0xx`, `HRLD0xxx`) to errors. Informational on the build-assertion attribute; the actual escalation happens via `<WarningsAsErrors>` on the consumer's csproj when this is `true`. |

## Where the properties live

The Herald.OSS NuGet ships `buildTransitive/Herald.OSS.props` which auto-applies
the `InterceptorsNamespaces` opt-in and marks both properties as
`CompilerVisibleProperty`. Consumers who reach the generator through a
`ProjectReference` (in-source-tree builds) need to mirror the
`CompilerVisibleProperty` items by hand — see
`tests/Interceptor.SmokeTests/Interceptor.SmokeTests.csproj` for the pattern.

## Reading the build assertion at runtime

```csharp
using System.Reflection;
using MMP.Herald.Build;

var marker = typeof(Program).Assembly
    .GetCustomAttribute<HeraldBuildAssertionAttribute>();

if (marker is null)
{
    // Either HeraldInterceptorsEnabled=false on build, or the Herald.OSS
    // NuGet didn't auto-apply its buildTransitive/Herald.OSS.props.
}
else
{
    Console.WriteLine(marker.InterceptorsEnabled);   // true / false
    Console.WriteLine(marker.BakedPolicies);         // "Pascal,Snake,Camel"
    Console.WriteLine(marker.InterceptedCallSites);  // int
    Console.WriteLine(marker.StrictMode);            // true / false
}
```

The lookup is trim-safe and AOT-safe — `Assembly.GetCustomAttribute<T>()` over a
plain attribute with init-only properties does not require dynamic-code
generation.
