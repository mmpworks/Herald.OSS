#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using MMP.Herald.Bootstrap;
using MMP.Herald.Quick;
using Xunit;

namespace MMP.Herald.OSS.Tests.Bootstrap;

/// <summary>
/// Pins the principal-review #8 + #12 + #14 hot-reload surface:
///
///   - OnReloadCompleted fires with a ReloadDiagnostics carrying the
///     outcome and a non-negative duration after a successful reload.
///   - OnReloadFailed fires when the JSON deserialise / build path
///     throws; the original exception surfaces to the caller AND to the
///     event.
///   - WatchFile remains a thin wrapper that constructs a
///     FileConfigReloadSource — direct UseReloadSource calls work too.
/// </summary>
public sealed class HotReloadEventTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly List<QuickLogResult> _liveResults = new();

    public void Dispose()
    {
        foreach (var live in _liveResults)
        {
            try { live.HotReloadBootstrap?.Dispose(); } catch { /* best-effort */ }
        }
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void OnReloadCompleted_fires_with_diagnostics_on_successful_reload()
    {
        var live = BuildHotReloadable(minLevel: "info");
        var hotReload = live.HotReloadBootstrap!;

        ReloadDiagnostics? observed = null;
        hotReload.OnReloadCompleted += diag => observed = diag;

        // Reload with a level change — exercises the level-only fast path.
        var json2 = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("debug")
            .WithHotReload()
            .ExportConfig();

        var outcome = hotReload.Reload(json2);

        outcome.Should().Be(HotReloadOutcome.Applied);
        observed.Should().NotBeNull("a successful reload must publish a ReloadDiagnostics");
        observed!.Outcome.Should().Be(HotReloadOutcome.Applied);
        observed.Exception.Should().BeNull();
        observed.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void OnReloadFailed_fires_when_reload_throws()
    {
        var live = BuildHotReloadable(minLevel: "info");
        var hotReload = live.HotReloadBootstrap!;

        ReloadDiagnostics? observed = null;
        hotReload.OnReloadFailed += diag => observed = diag;

        // Garbage JSON — Deserialize throws and OnReloadFailed fires.
        var act = () => hotReload.Reload("{ not valid herald config }");

        act.Should().Throw<Exception>();
        observed.Should().NotBeNull("a failed reload must publish a ReloadDiagnostics with the exception");
        observed!.Exception.Should().NotBeNull();
    }

    [Fact]
    public void WatchFile_adapts_through_FileConfigReloadSource()
    {
        // Smoke test: WatchFile must construct a FileConfigReloadSource
        // and Start it without throwing. The exact change-callback firing
        // is FileSystemWatcher-driven and flaky in CI; here we just pin
        // that the adapter wiring compiles and runs.
        var live = BuildHotReloadable(minLevel: "info");
        var path = WriteTempJson(QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("info")
            .WithHotReload()
            .ExportConfig());

        var act = () => live.HotReloadBootstrap!.WatchFile(path, debounceMs: 50);
        act.Should().NotThrow();
    }

    [Fact]
    public void UseReloadSource_routes_synthetic_source_through_callback()
    {
        var live = BuildHotReloadable(minLevel: "info");
        var hotReload = live.HotReloadBootstrap!;

        var observedSources = new List<string?>();
        hotReload.OnReloadCompleted += diag => observedSources.Add(diag.Path);

        var json2 = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("debug")
            .WithHotReload()
            .ExportConfig();

        var source = new SyntheticReloadSource(json2, sourceId: "synthetic://my-source");
        hotReload.UseReloadSource(source);
        source.Trigger();

        observedSources.Should().Contain("synthetic://my-source",
            "the source identifier passed into the callback must surface in ReloadDiagnostics.Path");
    }

    private QuickLogResult BuildHotReloadable(string minLevel)
    {
        var live = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel(minLevel)
            .WithHotReload()
            .BuildAndCommit();
        live.HotReloadBootstrap.Should().NotBeNull("WithHotReload() must yield a hot-reload bootstrap for these tests");
        _liveResults.Add(live);
        return live;
    }

    private string WriteTempJson(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"herald-watch-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contents);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Test-only IConfigReloadSource that immediately invokes the
    /// supplied callback when <see cref="Trigger"/> is called. Lets the
    /// test verify the bootstrap routes through UseReloadSource without
    /// touching FileSystemWatcher.
    /// </summary>
    private sealed class SyntheticReloadSource : IConfigReloadSource
    {
        private readonly string _initialJson;
        private readonly string _sourceId;
        private Action<string, string>? _callback;

        public SyntheticReloadSource(string initialJson, string sourceId)
        {
            _initialJson = initialJson;
            _sourceId = sourceId;
        }

        public void Start(Action<string, string> onConfigChanged)
        {
            _callback = onConfigChanged;
        }

        public void Trigger()
        {
            _callback?.Invoke(_sourceId, _initialJson);
        }

        public void Dispose() { }
    }
}
