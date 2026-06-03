#nullable enable

namespace MMP.Herald.Serilog.Sinks.SystemConsole.Themes;

/// <summary>
/// Elements styled by a console theme. Mirrors Serilog's
/// <c>Serilog.Sinks.SystemConsole.Themes.ConsoleThemeStyle</c> member-for-member
/// so a migrated <c>new AnsiConsoleTheme(new Dictionary&lt;ConsoleThemeStyle, string&gt;{...})</c>
/// custom palette resolves under <c>using MMP.Herald.Serilog;</c> with only the
/// one-namespace find-replace.
///
/// <para>
/// Herald's themed-console path keys its per-level ANSI styling off the level
/// members (<see cref="LevelVerbose"/>..<see cref="LevelFatal"/>). The
/// non-level members are accepted so a Serilog custom map compiles unchanged;
/// the level members are the ones that drive the rendered line today.
/// </para>
/// </summary>
public enum ConsoleThemeStyle
{
    /// <summary>Prevailing message text.</summary>
    Text,

    /// <summary>Punctuation and other secondary text.</summary>
    SecondaryText,

    /// <summary>Text that is less important than <see cref="SecondaryText"/>.</summary>
    TertiaryText,

    /// <summary>Text that signals a high likelihood of a problem.</summary>
    Invalid,

    /// <summary>The null literal.</summary>
    Null,

    /// <summary>Property and type names.</summary>
    Name,

    /// <summary>Strings.</summary>
    String,

    /// <summary>Numbers.</summary>
    Number,

    /// <summary>Boolean values.</summary>
    Boolean,

    /// <summary>All other scalar values, e.g. <see cref="System.Guid"/>.</summary>
    Scalar,

    /// <summary>Deprecated alias for <see cref="Scalar"/> (kept for source parity).</summary>
    Object,

    /// <summary>Level marker for verbose events.</summary>
    LevelVerbose,

    /// <summary>Level marker for debug events.</summary>
    LevelDebug,

    /// <summary>Level marker for information events.</summary>
    LevelInformation,

    /// <summary>Level marker for warning events.</summary>
    LevelWarning,

    /// <summary>Level marker for error events.</summary>
    LevelError,

    /// <summary>Level marker for fatal events.</summary>
    LevelFatal,
}
