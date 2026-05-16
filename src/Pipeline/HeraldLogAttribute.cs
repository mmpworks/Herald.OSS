#nullable enable

using System;

namespace MMP.Herald.Pipeline;

/// <summary>
/// Marks a static partial method for compile-time log method generation.
/// The Herald source generator emits the method body with:
///   - Early IsEnabled() guard (zero alloc on rejection)
///   - Pre-baked property names (no string allocation per call)
///   - Direct Log() call (no template parsing at runtime)
///
/// The containing class must also be partial.
///
/// Usage:
///   public static partial class GameLog
///   {
///       [HeraldLog(Level = "info", Category = "App",
///           Message = "Player {playerName} scored {points} points")]
///       public static partial void PlayerScored(
///           StructuredLogger logger, string playerName, int points);
///   }
///
///   // At call site:
///   GameLog.PlayerScored(logger, "Kael", 42);
///
/// The generator produces:
///   public static partial void PlayerScored(
///       StructuredLogger logger, string playerName, int points)
///   {
///       if (!logger.IsEnabled(KnownLogLevels.Info)) return;
///       logger.Log(KnownLogLevels.Info, new LogCategory("App"),
///           "Player {playerName} scored {points} points",
///           properties: new LogProperty[]
///           {
///               new("playerName", playerName),
///               new("points", points)
///           });
///   }
///
/// Performance: identical to hand-written LogMessage.Define delegates
/// but without the boilerplate. Zero allocation when level is disabled.
/// Template string is a compile-time constant, never parsed at runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class HeraldLogAttribute : Attribute
{
    /// <summary>
    /// Log level key: "trace", "debug", "info", "warn", "error", "fatal",
    /// or any custom level key registered in the level registry.
    /// </summary>
    public string Level { get; set; } = "info";

    /// <summary>
    /// Category name. Maps to a LogCategory instance.
    /// </summary>
    public string Category { get; set; } = "App";

    /// <summary>
    /// Message template with {PropertyName} placeholders.
    /// Property names must match the method parameter names (excluding the
    /// first parameter which must be StructuredLogger).
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Per-method naming-policy override. Values: <c>"pascal"</c> (default),
    /// <c>"camel"</c>, <c>"snake"</c>. When set, this method's property names
    /// are resolved against the named policy at compile time regardless of
    /// the project-wide <c>HeraldNamingPolicy</c> MSBuild property.
    /// </summary>
    /// <remarks>
    /// Default is <c>null</c> meaning "use the project setting." Useful when
    /// one or two methods need to match a downstream consumer's schema while
    /// the rest of the project uses the default policy. An unknown value is
    /// a build error (HERALD0411).
    /// </remarks>
    public string? NamingPolicy { get; set; }
}
