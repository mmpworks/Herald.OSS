#nullable enable

using System;
using System.IO;
using MMP.Herald.Addons.ManagementApi.Entities;
using Xunit;

namespace MMP.Herald.OSS.Tests.Addons.ManagementApi;

/// <summary>
/// Regression: WarnOrphanedSections used Debug.WriteLine, which is
/// [Conditional("DEBUG")] and compiles out of Release builds — the boot-time
/// validator meant to catch silently-dropped config sections was itself a
/// silent no-op in every shipped package. It now writes to stderr, which
/// exists in every build configuration.
/// </summary>
public sealed class OrphanedSectionWarningTests
{
    [Fact]
    public void WarnOrphanedSections_writes_to_stderr_in_all_build_configs()
    {
        var registry = EntityKindRegistry.CreateDefault();
        var original = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            registry.WarnOrphanedSections(["mystery_kind_section"]);
        }
        finally
        {
            Console.SetError(original);
        }

        var output = capture.ToString();
        Assert.Contains("[Herald] WARN", output);
        Assert.Contains("mystery_kind_section", output);
        Assert.Contains("no IEntityKindPolicy registered", output);
    }

    [Fact]
    public void WarnOrphanedSections_is_quiet_when_nothing_is_orphaned()
    {
        var registry = EntityKindRegistry.CreateDefault();
        var original = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            registry.WarnOrphanedSections([]);
        }
        finally
        {
            Console.SetError(original);
        }

        Assert.Equal(string.Empty, capture.ToString());
    }
}
