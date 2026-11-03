using MMP.Herald.Addons.Instrumentation;
using MMP.Herald.Quick;
using MMP.Herald.Serilog.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoggingPractices.Practices;

/// <summary>
/// Practice 6. One summary line per request, plus spans for the work inside it.
/// UseSerilogRequestLogging() installs RequestLoggingMiddleware, shaped by
/// RequestLoggingOptions. SpanScope logs entry and exit with the elapsed time.
/// </summary>
public static class CanonicalLogLines
{
    public static async Task RunAsync()
    {
        await using var herald = QuickLogBuilder.Create()
            .WithConsoleSink()
            .WithMinimumLevel("debug")
            .BuildAndCommit();
        var spans = herald.CreateSpanFactory();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddHerald(herald);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseSerilogRequestLogging(options =>
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms");
        app.MapGet("/orders/{id}", (string id) =>
        {
            using var span = SpanScope.Begin(herald.Logger, spans, "LoadOrder");
            return $"order {id}";
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var http = new HttpClient();
        var body = await http.GetStringAsync($"{address}/orders/42");
        Console.WriteLine($"  client received: {body}");

        await app.StopAsync();
    }
}
