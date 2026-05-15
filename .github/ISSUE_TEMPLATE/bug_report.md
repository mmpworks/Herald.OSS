---
name: Bug report
about: A defect in Herald.OSS — wrong behaviour, crash, or regression
title: "[bug] "
labels: bug
---

## What happened

<!-- A clear description of the bug. -->

## Reproduction

<!--
Minimal repro. Include the QuickLogBuilder configuration and the call
that misbehaves. A failing test is the gold standard; a small console
program is fine.
-->

```csharp
var result = QuickLogBuilder.Create()
    .WithNullSink()
    .WithMinimumLevel("trace")
    .BuildAndCommit();

result.Logger.Info(LogCategory.App, "...");
```

## Expected behaviour

<!-- What did you expect? -->

## Actual behaviour

<!-- What actually happened? Include stack traces if relevant. -->

## Environment

- Herald.OSS version:
- .NET runtime (`dotnet --info`):
- OS / arch:
- AOT / trimming enabled?

## Additional context

<!-- Anything else worth knowing — related issues, workarounds tried. -->
