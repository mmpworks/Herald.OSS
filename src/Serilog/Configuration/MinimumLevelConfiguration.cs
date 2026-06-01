#nullable enable

using MMP.Herald.Levels;
using MMP.Herald.Serilog.Core;
using MMP.Herald.Serilog.Events;

namespace MMP.Herald.Serilog.Configuration;

/// <summary>
/// Fluent <c>MinimumLevel.*</c> translator: maps each Serilog minimum-level
/// configuration call to the equivalent <see cref="MMP.Herald.Quick.QuickLogBuilder"/>
/// method on the root <see cref="LoggerConfiguration.Builder"/>.
///
/// <para>
/// Override ordering invariant (R-01 / R-02):
/// <c>ControlledBy</c> and <c>Override</c> calls accumulate. When <c>Override</c>
/// is called before <c>ControlledBy</c>, an Information-floor switch is auto-created
/// so that a subsequent <c>ControlledBy</c> call produces identical JSON to calling
/// them in the reverse order. All switch/override state lives in this class.
/// </para>
/// </summary>
public sealed class MinimumLevelConfiguration
{
    private readonly LoggerConfiguration _root;

    // Null until ControlledBy() or Override() is first called.
    // Auto-initialised at Information when Override() fires first (R-02).
    private LogLevelSwitch? _globalSwitch;

    // Null until the first Override() call. Accumulates entries on every call;
    // the same instance is reused so all entries are visible to the builder.
    private CategoryLevelSwitchMap? _overrides;

    internal MinimumLevelConfiguration(LoggerConfiguration root) => _root = root;

    // ── Named-verb shortcuts ──────────────────────────────────────────────

    /// <summary>Set the pipeline minimum level to <c>verbose</c>.</summary>
    public LoggerConfiguration Verbose()     => Is(LogEventLevel.Verbose);

    /// <summary>Set the pipeline minimum level to <c>debug</c>.</summary>
    public LoggerConfiguration Debug()       => Is(LogEventLevel.Debug);

    /// <summary>Set the pipeline minimum level to <c>information</c>.</summary>
    public LoggerConfiguration Information() => Is(LogEventLevel.Information);

    /// <summary>Set the pipeline minimum level to <c>warning</c>.</summary>
    public LoggerConfiguration Warning()     => Is(LogEventLevel.Warning);

    /// <summary>Set the pipeline minimum level to <c>error</c>.</summary>
    public LoggerConfiguration Error()       => Is(LogEventLevel.Error);

    /// <summary>
    /// Set the pipeline minimum level to <c>fatal</c>.
    /// <para>
    /// P0 rename-trap: Serilog calls this level <c>Fatal</c>; the old Herald
    /// name was <c>Critical</c>. The Herald key is "fatal" — never "critical".
    /// </para>
    /// </summary>
    public LoggerConfiguration Fatal()       => Is(LogEventLevel.Fatal);

    // ── Generic Is() ─────────────────────────────────────────────────────

    /// <summary>
    /// Set the pipeline minimum level to <paramref name="minimumLevel"/>.
    /// Maps to <c>QuickLogBuilder.WithMinimumLevel(key)</c>.
    /// </summary>
    public LoggerConfiguration Is(LogEventLevel minimumLevel)
    {
        _root.Builder.WithMinimumLevel(SerilogLevelMap.ToHerald(minimumLevel).Key);
        return _root;
    }

    // ── Dynamic level switch ──────────────────────────────────────────────

    /// <summary>
    /// Wire a <see cref="LoggingLevelSwitch"/> as the pipeline's dynamic level
    /// controller. Maps to <c>QuickLogBuilder.WithFastDynamicLevel</c>.
    ///
    /// <para>
    /// R-01: calling this after <see cref="Override"/> (reverse order) must
    /// produce identical JSON to calling Override after ControlledBy. Existing
    /// overrides in <c>_overrides</c> are preserved when the switch is replaced.
    /// </para>
    /// </summary>
    public LoggerConfiguration ControlledBy(LoggingLevelSwitch levelSwitch)
    {
        // Replace the global switch; _overrides accumulates across both orderings
        // so R-01 holds: same switch + same map = same JSON regardless of order.
        _globalSwitch = levelSwitch.Native;
        ApplyDynamicLevel();
        return _root;
    }

    /// <summary>
    /// W2: seed the dynamic global floor from a parsed <see cref="LogEventLevel"/>.
    ///
    /// <para>
    /// This is the appsettings fix for the Default↔Override clobber. The reader
    /// must NOT call <see cref="Is"/> (the cheap static floor) for the Default when
    /// an Override block exists — once <see cref="Override"/> activates the dynamic
    /// switch, the two floor mechanisms would disagree and the static Default is
    /// lost (the auto-created switch seeds at Information, silently raising a Warning
    /// floor). Routing Default through this overload seeds the dynamic switch at the
    /// parsed level FIRST, so the later <see cref="Override"/> call leaves it intact
    /// (its <c>??=</c> only seeds Information when no switch exists yet).
    /// </para>
    /// </summary>
    /// <param name="defaultLevel">The parsed global Default level.</param>
    public LoggerConfiguration ControlledBy(LogEventLevel defaultLevel)
    {
        // Seed the dynamic floor from the parsed level. A subsequent Override() sees
        // a non-null _globalSwitch and leaves this seed in place — the Default holds.
        _globalSwitch = new LogLevelSwitch(SerilogLevelMap.ToHerald(defaultLevel));
        ApplyDynamicLevel();
        return _root;
    }

    // ── Category overrides ────────────────────────────────────────────────

    /// <summary>
    /// Add a per-source-context (category) level override.
    /// Calls accumulate — a second call adds to the map rather than replacing it.
    ///
    /// <para>
    /// R-02: calling this before <see cref="ControlledBy"/> must not throw.
    /// An Information-floor switch is auto-created so Override-then-ControlledBy
    /// and ControlledBy-then-Override produce identical JSON.
    /// </para>
    /// </summary>
    public LoggerConfiguration Override(string source, LogEventLevel minimumLevel)
    {
        // R-02: auto-create an Information-floor switch if none has been set yet.
        // A subsequent ControlledBy() replaces _globalSwitch but _overrides
        // is preserved — so both orderings wire the same switch+map state.
        _globalSwitch ??= new LogLevelSwitch(KnownLogLevels.Information);

        // Lazy-init and accumulate. The same CategoryLevelSwitchMap instance is
        // passed to WithFastDynamicLevel every time so the builder always sees the
        // full accumulated set of overrides via the live reference.
        _overrides ??= new CategoryLevelSwitchMap();
        _overrides.SetCategoryLevel(source, SerilogLevelMap.ToHerald(minimumLevel));

        ApplyDynamicLevel();
        return _root;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    // Push the current _globalSwitch + optional _overrides into the builder.
    // WithFastDynamicLevel replaces the previous wiring on each call (last call
    // wins in QuickLogBuilder), so calling this after every mutation is correct.
    private void ApplyDynamicLevel()
    {
        if (_globalSwitch is null) return;

        if (_overrides is null)
            _root.Builder.WithFastDynamicLevel(_globalSwitch);
        else
            _root.Builder.WithFastDynamicLevel(_globalSwitch, _overrides);
    }
}
