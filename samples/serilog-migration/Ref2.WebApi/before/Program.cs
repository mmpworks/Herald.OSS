using Serilog;

// Ref2.WebApi — the ASP.NET Core case: bootstrap logger + UseSerilog host wiring,
// UseSerilogRequestLogging middleware, appsettings.json config, one enricher.
// Starts Kestrel on a fixed loopback port, self-requests once so request logging
// fires, then shuts down — exercising the whole Serilog ASP.NET surface headless.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .Enrich.WithProperty("Service", "Ref2.WebApi")
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5111");

builder.Host.UseSerilog((context, services, configuration) => configuration
    .WriteTo.Console()
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.WithProperty("Service", "Ref2.WebApi"));

var app = builder.Build();

app.UseSerilogRequestLogging();

app.MapGet("/", () =>
{
    Log.Information("Handled root request for {Resource}", "/");
    return Results.Ok(new { status = "ok" });
});

await app.StartAsync();
Log.Information("WebApi started on {Url}", "http://127.0.0.1:5111");

using (var http = new HttpClient())
{
    var body = await http.GetStringAsync("http://127.0.0.1:5111/");
    Log.Information("Self-request returned {Body}", body);
}

Log.Warning("Cache miss for {Key}", "user:42");
await app.StopAsync();
Log.Information("WebApi stopped");
await Log.CloseAndFlushAsync();
return 0;
