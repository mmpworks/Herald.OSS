#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi.Entities.Policies;

/// <summary>
/// Category styles clear-then-replay so a saved config with N entries
/// produces exactly N entries on restore — removed categories really
/// disappear on the next load, never linger as a stale leftover.
/// </summary>
internal sealed class CategoryStyleEntityPolicy : IEntityKindPolicy
{
    public string Kind => "categoryStyle";

    public bool HasSectionInConfig(JsonLoggingConfig config) =>
        config.CategoryStyles is { Count: > 0 };

    public void RestoreFromConfig(QuickLogBuilder builder, JsonLoggingConfig config)
    {
        var source = config.CategoryStyles;
        if (source is null) return;
        builder.ClearCategoryStyles();
        foreach (var s in source)
            builder.CategoryStyles.Add(s.CategoryName, s.ColorName, s.UseBold, s.UseItalic, s.BackgroundColorName);
    }
}
