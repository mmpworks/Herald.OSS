#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi.Entities.Policies;

/// <summary>
/// Property styles clear-then-replay, parallel to category styles. The
/// silent-drop bug that drove this whole refactor lived right here —
/// the inlined restore block was absent and PropertyStyles round-tripped
/// to zero on every save. With the policy registered through the
/// registry, "is the section restored?" reduces to "is the kind
/// registered?" and the boot-time validator answers that explicitly.
///
/// <see cref="MMP.Herald.Quick.QuickLogBuilder.PropertyStyles"/>'s
/// <c>Add</c> takes a non-nullable color name while the category
/// equivalent accepts <c>string?</c>. The null-coalesce here is a
/// local workaround until the two Set APIs are aligned.
/// </summary>
internal sealed class PropertyStyleEntityPolicy : IEntityKindPolicy
{
    public string Kind => "propertyStyle";

    public bool HasSectionInConfig(JsonLoggingConfig config) =>
        config.PropertyStyles is { Count: > 0 };

    public void RestoreFromConfig(QuickLogBuilder builder, JsonLoggingConfig config)
    {
        var source = config.PropertyStyles;
        if (source is null) return;
        builder.ClearPropertyStyles();
        foreach (var s in source)
            builder.PropertyStyles.Add(s.PropertyName, s.ColorName ?? string.Empty, s.UseBold, s.UseItalic, s.BackgroundColorName);
    }
}
