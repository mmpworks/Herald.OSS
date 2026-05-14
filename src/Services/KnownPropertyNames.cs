#nullable enable

namespace MMP.Herald.Services;

/// <summary>
/// Well-known property names used by Herald's default property styles.
/// Use these when adding or removing property styles via QuickLogBuilder,
/// or when creating structured log events with template properties.
///
/// Usage:
///   builder.WithPropertyStyle(KnownPropertyNames.UserId, KnownAnsiColors.Cyan, bold: true)
///   builder.WithoutPropertyStyle(KnownPropertyNames.UserId)
///   logger.Info(LogCategory.App, "User {UserId} logged in", new LogProperty(KnownPropertyNames.UserId, id))
/// </summary>
public static class KnownPropertyNames
{
    // -- Identity --

    public const string UserId = "userId";
    public const string UserName = "userName";
    public const string EntityId = "entityId";
    public const string EntityName = "entityName";

    // -- Actions and operations --

    public const string Action = "action";
    public const string Operation = "operation";
    public const string Status = "status";
    public const string Result = "result";

    // -- Values and changes --

    public const string Value = "value";
    public const string Delta = "delta";
    public const string Count = "count";
    public const string Amount = "amount";
    public const string Duration = "duration";
    public const string Elapsed = "elapsed";

    // -- Location and context --

    public const string Path = "path";
    public const string Source = "source";
    public const string Target = "target";
    public const string Endpoint = "endpoint";

    // -- Error and diagnostic --

    public const string Error = "error";
    public const string Reason = "reason";
    public const string Exception = "exception";
    public const string StackTrace = "stackTrace";

    // -- Classification --

    public const string Category = "category";
    public const string Type = "type";
    public const string Kind = "kind";
    public const string Tag = "tag";

    // -- Timing --

    public const string Timestamp = "timestamp";
    public const string TimeOfDay = "timeOfDay";
}
