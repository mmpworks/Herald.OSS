#nullable enable

// Layer-2 mirror: SerilogWebHostBuilderExtensions — UseSerilog() on IWebHostBuilder.
//
// CROSS-PLAN GAP — P6 does not implement IWebHostBuilder.UseSerilog(...).
// Real Serilog.AspNetCore ships this surface for legacy WebHost-style bootstrapping:
//
//   WebHost.CreateDefaultBuilder(args).UseSerilog().Build();
//
// This stub is present so the Layer-2 assembly declares the type (preventing a compile
// error when the consumer references it), but every overload throws NotSupportedException
// with an actionable diagnostic message directing the consumer to use IHostBuilder or
// IHostApplicationBuilder instead.
//
// To resolve: implement IWebHostBuilder.UseSerilog in P6
// (src/Serilog.AspNetCore/SerilogHostBuilderExtensions.cs) and then update this
// file to be a one-line forward like the other Layer-2 extension classes.
//
// DRY-tripwire allowlist: the NotSupportedException throws below are the ONLY permitted
// non-forward expressions in this assembly. Each names the reason (P6 gap) explicitly.

using System;
using Microsoft.AspNetCore.Hosting;
using MMP.Herald.Pipeline;
using MMP.Herald.Serilog.Configuration;

namespace Serilog;

/// <summary>
/// Extension methods that add Herald request logging to the legacy
/// <see cref="IWebHostBuilder"/> surface, mirroring the real
/// <c>Serilog.SerilogWebHostBuilderExtensions</c>.
///
/// <para>
/// <strong>CROSS-PLAN GAP:</strong> P6 (<c>MMP.Herald.Serilog.AspNetCore</c>) does not
/// yet implement <c>IWebHostBuilder.UseSerilog(...)</c>. Every overload below throws
/// <see cref="NotSupportedException"/> with an actionable message. Migrate to
/// <c>IHostBuilder.UseSerilog(...)</c> or <c>IHostApplicationBuilder.UseSerilog(...)</c>
/// from <see cref="Microsoft.Extensions.Hosting.SerilogHostBuilderExtensions"/> instead.
/// </para>
/// </summary>
public static class SerilogWebHostBuilderExtensions
{
    private const string GapMessage =
        "IWebHostBuilder.UseSerilog is not yet implemented in Herald's P6 layer. " +
        "Migrate to IHostBuilder.UseSerilog or IHostApplicationBuilder.UseSerilog " +
        "(Microsoft.Extensions.Hosting.SerilogHostBuilderExtensions). " +
        "Track resolution: implement IWebHostBuilder.UseSerilog in " +
        "src/Serilog.AspNetCore/SerilogHostBuilderExtensions.cs (P6 gap).";

    /// <summary>
    /// Not yet implemented — see the class-level documentation for the cross-plan gap notice.
    /// </summary>
    public static IWebHostBuilder UseSerilog(
        this IWebHostBuilder builder,
        StructuredLogger logger)
        => throw new NotSupportedException(GapMessage);

    /// <summary>
    /// Not yet implemented — see the class-level documentation for the cross-plan gap notice.
    /// </summary>
    public static IWebHostBuilder UseSerilog(
        this IWebHostBuilder builder,
        Action<WebHostBuilderContext, IServiceProvider, LoggerConfiguration> configure)
        => throw new NotSupportedException(GapMessage);

    /// <summary>
    /// Not yet implemented — see the class-level documentation for the cross-plan gap notice.
    /// </summary>
    public static IWebHostBuilder UseSerilog(
        this IWebHostBuilder builder)
        => throw new NotSupportedException(GapMessage);
}
