#nullable enable
#if NET9_0_OR_GREATER

using FluentAssertions;
using MMP.Herald.Quick;

namespace MMP.Herald.OSS.Tests.Serilog.Configuration;

/// <summary>
/// Core P2 test harness: a Serilog-shaped translator output and an equivalent
/// hand-built <see cref="QuickLogBuilder"/> must produce identical JSON via
/// <see cref="QuickLogBuilder.ExportConfigJson"/>.
///
/// <para>
/// This catches wrong-sink-kind, wrong-level-key, and missed-field bugs in the
/// translator layer before <c>LoggerConfiguration</c> (P2 Task 2) lands.
/// </para>
///
/// <para>
/// Callers build both builders independently, then pass the already-built instances.
/// This avoids any forward-reference to <c>LoggerConfiguration</c> (which does not
/// exist yet) while keeping the oracle itself compilable and callable today.
/// </para>
/// </summary>
public static class TranslatorParityOracle
{
    /// <summary>
    /// Assert that <paramref name="fromTranslator"/> and <paramref name="fromDirect"/>
    /// produce identical pipeline JSON. Both builders should represent the same
    /// logical configuration — one built via the Serilog-compat translator path,
    /// the other via direct <see cref="QuickLogBuilder"/> calls.
    /// </summary>
    /// <param name="fromTranslator">
    /// A builder populated by the Serilog-shaped translator under test.
    /// </param>
    /// <param name="fromDirect">
    /// A builder populated by the equivalent direct Herald API calls.
    /// </param>
    public static void AssertSameShape(QuickLogBuilder fromTranslator, QuickLogBuilder fromDirect)
    {
        var translatorJson = fromTranslator.ExportConfigJson();
        var directJson = fromDirect.ExportConfigJson();

        translatorJson.Should().Be(directJson,
            "the translator must produce the same pipeline JSON as the equivalent direct builder call");
    }
}

#endif
