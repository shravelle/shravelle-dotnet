// Shravelle APM for .NET.
//
//   var builder = WebApplication.CreateBuilder(args);
//   builder.AddShravelle(o => o.ServiceName = "orders-api");   // SHRAVELLE_API_KEY / SHRAVELLE_ENDPOINT from env
//
// Traces (ASP.NET Core, HttpClient, SqlClient), metrics (ASP.NET Core, HTTP,
// runtime/GC) and ILogger logs are exported as OTLP/protobuf to Shravelle.
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shravelle;

public sealed class ShravelleOptions
{
    public string Endpoint { get; set; } = Env("SHRAVELLE_ENDPOINT", "http://localhost:4318");
    public string ApiKey { get; set; } = Env("SHRAVELLE_API_KEY", "");
    public string ServiceName { get; set; } = Env("SHRAVELLE_SERVICE_NAME", Env("OTEL_SERVICE_NAME", AppDomain.CurrentDomain.FriendlyName));
    public string ServiceVersion { get; set; } = Env("SHRAVELLE_SERVICE_VERSION", "");
    public string Environment { get; set; } = Env("SHRAVELLE_ENV", "production");
    public double SampleRate { get; set; } = double.TryParse(Env("SHRAVELLE_SAMPLE_RATE", "1"), out var r) ? r : 1;
    internal static string Env(string k, string d) => System.Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : d;
}

public static class ShravelleExtensions
{
    public static WebApplicationBuilder AddShravelle(this WebApplicationBuilder builder, Action<ShravelleOptions>? configure = null)
    {
        var o = new ShravelleOptions();
        configure?.Invoke(o);
        ShravelleApi.Options = o;
        var endpoint = o.Endpoint.TrimEnd('/');
        void Exporter(OtlpExporterOptions e, string path)
        {
            e.Endpoint = new Uri(endpoint + path);
            e.Protocol = OtlpExportProtocol.HttpProtobuf;
            e.Headers = "x-api-key=" + o.ApiKey;
        }
        var resource = ResourceBuilder.CreateDefault()
            .AddService(o.ServiceName, serviceVersion: o.ServiceVersion)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment.name"] = o.Environment,
                ["host.name"] = System.Environment.MachineName,
                ["telemetry.distro.name"] = "shravelle-dotnet",
            });
        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.SetResourceBuilder(resource)
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(o.SampleRate)))
                .AddSource(ShravelleApi.Source.Name)
                .AddAspNetCoreInstrumentation(a => a.RecordException = true)
                .AddHttpClientInstrumentation(h => h.RecordException = true)
                .AddSqlClientInstrumentation(s => { s.SetDbStatementForText = true; s.RecordException = true; })
                .AddOtlpExporter(e => Exporter(e, "/v1/traces")))
            .WithMetrics(m => m.SetResourceBuilder(resource)
                .AddMeter(ShravelleApi.Meter.Name)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter((e, reader) => { Exporter(e, "/v1/metrics"); reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 30000; }));
        builder.Logging.AddOpenTelemetry(l =>
        {
            l.SetResourceBuilder(resource);
            l.IncludeFormattedMessage = true;
            l.IncludeScopes = true;
            l.AddOtlpExporter(e => Exporter(e, "/v1/logs"));
        });
        return builder;
    }
}

/// <summary>Helpers usable anywhere in the app.</summary>
public static class ShravelleApi
{
    internal static ShravelleOptions Options = new();
    public static readonly ActivitySource Source = new("Shravelle");
    public static readonly System.Diagnostics.Metrics.Meter Meter = new("Shravelle");

    public static void NoticeError(Exception ex)
    {
        var a = Activity.Current;
        if (a == null) return;
        a.RecordException(ex); // OpenTelemetry.Trace extension (works on .NET 6+)
        a.SetStatus(ActivityStatusCode.Error, ex.Message);
    }

    public static void SetUser(string id) => Activity.Current?.SetTag("enduser.id", id);
    public static void AddAttribute(string key, object? value) => Activity.Current?.SetTag(key, value);
    public static void RecordEvent(string name, IDictionary<string, object?>? attrs = null) =>
        Activity.Current?.AddEvent(new ActivityEvent(name, tags: attrs == null ? default : new ActivityTagsCollection(attrs)));

    /// <summary>using (ShravelleApi.Segment("pricing.calculate")) { ... }</summary>
    public static Activity? Segment(string name) => Source.StartActivity(name);

    public static async Task RecordDeploymentAsync(string version, string commit = "", string description = "")
    {
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, Options.Endpoint.TrimEnd('/') + "/v1/deployments")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { service = Options.ServiceName, version, commit, description, user = System.Environment.UserName }),
        };
        req.Headers.Add("X-API-Key", Options.ApiKey);
        (await http.SendAsync(req)).EnsureSuccessStatusCode();
    }
}
