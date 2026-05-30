namespace MMP.Herald.Levels;

/// <summary>
/// Canonical keys for every built-in log level. These are <c>const string</c>
/// so they are usable in attribute arguments and pattern-matched literals;
/// they match the <c>Key</c> fields on the <see cref="LogLevel"/> records
/// declared in <c>KnownLogLevels.cs</c>.
///
/// <para>
/// <b>Why the separate file.</b> <c>MMP.Herald.Generators</c> links this
/// file into its netstandard2.0 assembly so the HERALD007 analyzer can
/// cross-check <c>[HeraldLog(Level = "...")]</c> strings at edit time.
/// Typos like <c>"infro"</c> surface as IDE squiggles instead of
/// compiling against a non-existent <c>KnownLogLevels.Infro</c> field.
/// A rename here is a compile-time break on both sides.
/// </para>
///
/// <para>
/// Generators targets netstandard2.0, so this file must stay language-
/// feature-conservative: <c>const string</c> only, no records.
/// </para>
/// </summary>
public static class KnownLogLevelKeys
{
    public const string Verbose = "verbose";
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Notice = "notice";
    public const string Success = "success";
    public const string Fatal = "fatal";
    public const string Security = "security";
    public const string Metric = "metric";
}
