# AOT and trimming

A guide for shipping a .NET application that uses Herald.OSS with
**native AOT** — the publish mode that bakes everything into a
single platform-specific binary and removes the .NET runtime's
just-in-time compiler.

This is opt-in. If you ship a normal .NET app with the JIT (the
default), this guide doesn't apply — Herald.OSS works fine and
there is nothing to configure. If you ship for mobile, locked-down
consoles, or any environment that forbids runtime code generation,
read on.

## What AOT actually does

In a normal .NET build:

```
build time   →   ship time   →   first call (per method)   →   steady state
  C# → IL          IL on disk     JIT translates IL → machine    machine code
                                  code, caches the result        runs at full speed
```

The just-in-time compiler does that translation on first use. It's
fast — microseconds per method — but it's still work the runtime
has to do, and it requires the ability to generate code at runtime.

AOT moves the translation to publish time:

```
build time   →   publish time          →   ship time           →   steady state
  C# → IL          IL → native binary     single .exe file           machine code
                   (whole-program walk)   no .NET runtime            runs at full speed
                                          needed alongside
```

`dotnet publish -p:PublishAot=true` walks your application's reachable
code, compiles every method to native instructions, and links them
into one platform-specific executable. There's no JIT in the
shipped binary. There's no IL on disk. There's no assembly loading
at runtime.

What you get:

- **Faster start.** The first call to each method is already native
  code. A game's first frame isn't fighting with background JIT
  work.
- **Smaller deployments.** Trimming removes types nothing
  references. Sink packages you don't use don't ship.
- **Mobile and locked-down platforms.** iOS forbids JIT entirely.
  Some console SDKs do too. AOT is the only way in.

What you give up:

- **Reflection over arbitrary types.** Code that does
  `Type.GetType(string)` and then probes properties cannot be
  proven safe by the trimmer. It either fails to publish or
  silently breaks at runtime.
- **Some publish-time speed.** AOT publish is slower than a normal
  publish — the compiler is doing more work.

Herald.OSS is built to work without reflection on the hot path.
That's what makes the AOT story short.

## What Herald.OSS already does

The OSS package declares itself AOT-compatible. The csproj sets:

```xml
<IsAotCompatible>true</IsAotCompatible>
<EnableAotAnalyzer>true</EnableAotAnalyzer>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
```

What those flags do, in plain terms:

- `IsAotCompatible` is a promise to consumers: "I've checked this
  package for AOT and it's clean." A consumer who depends on Herald
  doesn't inherit any AOT warnings from us.
- `EnableAotAnalyzer` turns on the .NET analyzer that watches for
  patterns AOT can't support — `Activator.CreateInstance`,
  `Type.GetType(string)`, reflection-emit, generic virtual methods
  on open types. If anything in the source regresses, the OSS build
  itself catches it before the change can ship.
- `EnableTrimAnalyzer` does the same for the trimmer: it warns
  about code paths that would silently disappear when an unused
  type gets stripped.

JSON config is the place where naive .NET code most often trips
AOT. The OSS package routes every JSON path through
**source-generated** serializers — the JSON shape is known at
compile time, the code to read and write it is emitted by a
generator, and reflection never enters the picture. The generated
context lives at
`src/Configuration/HeraldJsonContext.cs`. Operators publishing AOT
don't see `IL2026` (`RequiresUnreferencedCode`) or `IL3050`
(`RequiresDynamicCode`) warnings from the config path.

## Setting up a consumer for AOT

A minimal AOT-publishable application:

```xml
<!-- yourapp.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>

    <IsAotCompatible>true</IsAotCompatible>
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Herald.OSS" Version="0.1.0" />
  </ItemGroup>
</Project>
```

`IsAotCompatible` keeps the analyzer on at build time — you'll
catch AOT regressions in your own code as warnings. `PublishAot`
controls whether the publish step actually produces a native
binary.

Publish:

```bash
dotnet publish -c Release -r win-x64
# or linux-x64, osx-arm64, etc.
```

A native binary lands in `bin/Release/net10.0/win-x64/publish/`.
No `Herald.OSS.dll` alongside it — the trimmer has already pulled
the parts your app references into the native exe.

One small gotcha: you need the **AOT workload** installed for the
publish to actually produce a native binary on Windows:

```bash
dotnet workload install aot
```

Without it, the publish succeeds quietly but falls back to a
self-contained managed build — the managed DLLs show up next to
the exe instead of being linked in. If you see DLLs after
publishing, the workload isn't installed.

## Trimming and unused sinks

The .NET trimmer removes types nothing references. For
Herald.OSS specifically, this means:

```
You reference:                    Your binary contains:
─────────────────────             ─────────────────────────────────
Herald.OSS                    Kernel, pipeline, Quick builder,
                                  + just the built-in sinks you
                                  reached via WithConsoleSink etc.
Herald.Sinks.HttpJson         The HTTP/JSON sink provider +
                                  the bits of HttpClient it uses.
(referenced but not called)       Pulled in only if your code
Herald.Sinks.Foo              actually constructs Foo. If
                                  trimmer can prove the package
                                  is unreachable, it's removed.
```

This is automatic. You don't write trim hints. The shipped
packages follow the rules — no reflection probing into unrelated
types — so the trimmer can be confident.

## What's already verified

These pieces of the OSS package are tested as AOT-clean:

| Surface | How it stays AOT-clean |
|---|---|
| `JsonLoggingConfig` parse/emit | source-generated `HeraldJsonContext` |
| `LogEvent`, `LogLevel`, `LogCategory`, `LogProperty` | plain records, no reflection |
| `ILogger`, `IKernelSink` | one-method interfaces, virtual dispatch |
| Pipeline decorators | each one written without reflection |
| `LoggingRuntimeBootstrap.Bootstrap` | pure data transformation |
| `LogSinkProviderRegistry.Default` | dictionary keyed by string |
| Source-gen overloads (`[HeraldLog]` and friends) | emit IL at build time, no runtime codegen |

If you build a custom sink and want it to be AOT-clean, two rules:

1. **No `JsonSerializer.Serialize<T>(value)`** without a
   `JsonTypeInfo<T>`. Use `Utf8JsonWriter` directly, or add a
   source-generated context for your sink's payload type.
2. **No `Type.GetType(string)`** to look up types. Hold typed
   references.

The analyzer on your project will tell you if you slip. Treat
`IL2026` and `IL3050` warnings as errors during AOT work.

## Limits in v0.1.0

A few things to know honestly:

- **No CI-gated AOT sample yet.** v0.1.0 ships the AOT-clean source
  and the analyzer; a separate AOT publish + boot test will land
  in a later milestone. Until it does, the inventory is verified
  by the OSS build's analyzer warnings.
- **Third-party sinks vary.** Built-in sinks (console, bridge,
  channel, audit, file, network) are AOT-clean. Sink packages from
  outside the Herald ecosystem may not be — check their csproj
  for `IsAotCompatible`. If a sink package depends on a library
  with native dependencies and reflection paths (`Confluent.Kafka`,
  some database client libraries), AOT compatibility belongs to
  that library, not to Herald.

## Troubleshooting

**"My publish output has DLLs next to the .exe."**
The AOT workload isn't installed. Run `dotnet workload install aot`
and republish.

**"I'm seeing IL2026 / IL3050 warnings from my own code."**
The analyzer found a reflection path the trimmer can't prove safe.
Common cases: `JsonSerializer.Serialize(value)` without a
source-generated context; `Activator.CreateInstance(type)`;
`Type.GetType(name)`. Replace with a typed equivalent.

**"Trimming removed a sink I actually use."**
You reference the sink package but never call into it through
reachable code. Either call the relevant `With*` method on the
builder (which marks the provider as reachable) or add a `DynamicDependency`
hint on a method your code does call.

**"Build is fine, runtime throws `MissingMethodException`."**
A class field, constructor, or method got trimmed because nothing
reachable references it. Usually means a code path is going through
reflection. The trim analyzer should have warned at build time —
look back through warnings, not just errors.

## Where to look next

- [`architecture.md`](architecture.md) — the three-layer model
  Herald.OSS uses; useful when reading trim warnings.
- [`building-sinks.md`](building-sinks.md) — sink shape, including
  the AOT-clean patterns shipped sinks follow.
- [`kernel-sink-pattern.md`](kernel-sink-pattern.md) — kernel-path
  sinks have no extra AOT cost; the interface dispatch is the same
  shape whether JIT'd or AOT'd.
- `src/Configuration/HeraldJsonContext.cs` — the source-generated
  JSON context.
- [Microsoft's Native AOT
  docs](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
  — for the .NET-side details on `PublishAot`, trim warnings, and
  workloads.
