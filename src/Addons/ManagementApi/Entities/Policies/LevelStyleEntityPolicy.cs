#nullable enable

using MMP.Herald.Configuration.Json;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi.Entities.Policies;

/// <summary>
/// Level styles are upsert-by-key. <c>WithLevelStyle</c> overwrites the
/// existing entry for the same key, so the policy does not clear before
/// replay — it just walks the section in order, last-writer-wins per key.
/// That asymmetry with category / property restore is explicit at the
/// policy level so adding a fourth style kind has a worked example
/// covering both shapes.
/// </summary>
internal sealed class LevelStyleEntityPolicy : IEntityKindPolicy
{
    public string Kind => "levelStyle";

    public bool HasSectionInConfig(JsonLoggingConfig config) =>
        config.LevelStyles is { Count: > 0 };

    public void RestoreFromConfig(QuickLogBuilder builder, JsonLoggingConfig config)
    {
        var source = config.LevelStyles;
        if (source is null) return;
        foreach (var s in source)
            builder.WithLevelStyle(s.LevelKey, s.ColorName, s.UseBold, s.UseItalic, s.BackgroundColorName);
    }
}
