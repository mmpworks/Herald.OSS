// Copyright (c) 2026 MMPWorks LLC
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.
#nullable enable

using System;
using System.Collections.Generic;
using MMP.Herald.Levels;
using MMP.Herald.Quick;

namespace MMP.Herald.Addons.ManagementApi;

/// <summary>
/// Level-related management operations on <see cref="HeraldManagementApi"/>:
/// the global minimum level, the custom-level registry, the canonical
/// level order, and the per-level display style overrides. The facade
/// holds the public surface; this class holds the bodies the facade
/// delegates to.
/// </summary>
internal sealed class LevelManagement
{
    private readonly IManagementContext _ctx;

    public LevelManagement(IManagementContext ctx)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
    }

    // ── Minimum level ─────────────────────────────────────────────────

    public ManagementResult SetMinimumLevel(string level)
    {
        if (_ctx.EnsureAuthorized(nameof(SetMinimumLevel)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        _ctx.Builder.WithMinimumLevel(level);
        return _ctx.AutoCommitOrStage($"Minimum level set to '{level}'.");
    }

    // ── Custom levels ─────────────────────────────────────────────────

    public IReadOnlyList<LogLevel> GetCustomLevels() => _ctx.Builder.GetCustomLevels();

    public ManagementResult AddCustomLevel(string key, string displayName)
    {
        if (_ctx.EnsureAuthorized(nameof(AddCustomLevel)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        _ctx.Builder.WithCustomLevel(key, displayName);
        return _ctx.AutoCommitOrStage($"Custom level '{key}' ({displayName}) added.");
    }

    public ManagementResult RemoveCustomLevel(string key)
    {
        if (_ctx.EnsureAuthorized(nameof(RemoveCustomLevel)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _ctx.Builder.WithoutCustomLevel(key);
        return _ctx.AutoCommitOrStage($"Custom level '{key}' removed.");
    }

    public ManagementResult ClearCustomLevels()
    {
        if (_ctx.EnsureAuthorized(nameof(ClearCustomLevels)) is { } denied) return denied;
        _ctx.Builder.ClearCustomLevels();
        return _ctx.AutoCommitOrStage("All custom log levels cleared.");
    }

    public ManagementResult SetLevelOrder(IEnumerable<string>? keys)
    {
        if (_ctx.EnsureAuthorized(nameof(SetLevelOrder)) is { } denied) return denied;
        _ctx.Builder.WithLevelOrder(keys);
        var order = _ctx.Builder.GetLevelOrder();
        var summary = order is null || order.Count == 0
            ? "Level order reset to canonical default."
            : $"Level order set to [{string.Join(", ", order)}].";
        return _ctx.AutoCommitOrStage(summary);
    }

    // ── Level styles ──────────────────────────────────────────────────

    public IReadOnlyList<LevelStyleInfo> GetLevelStyles()
    {
        var inspection = _ctx.Builder.Inspect();
        return inspection.LevelStyles ?? [];
    }

    public ManagementResult SetLevelStyle(string levelKey, string colorName,
        bool bold, bool italic, string? backgroundColorName)
    {
        if (_ctx.EnsureAuthorized(nameof(SetLevelStyle)) is { } denied) return denied;
        ArgumentException.ThrowIfNullOrWhiteSpace(levelKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(colorName);
        _ctx.Builder.WithLevelStyle(levelKey, colorName, bold, italic, backgroundColorName);
        return _ctx.AutoCommitOrStage($"Level style for '{levelKey}' set to {colorName}{(bold ? " bold" : "")}{(italic ? " italic" : "")}.");
    }

    public ManagementResult RemoveLevelStyle(string levelKey)
    {
        if (_ctx.EnsureAuthorized(nameof(RemoveLevelStyle)) is { } denied) return denied;
        _ctx.Builder.WithoutLevelStyle(levelKey);
        return _ctx.AutoCommitOrStage($"Level style override for '{levelKey}' removed.");
    }

    public ManagementResult ClearLevelStyles()
    {
        if (_ctx.EnsureAuthorized(nameof(ClearLevelStyles)) is { } denied) return denied;
        _ctx.Builder.ClearLevelStyles();
        return _ctx.AutoCommitOrStage("All level style overrides cleared. Defaults restored.");
    }
}
