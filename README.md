# .NET (ASP.NET Core, workers)

```xml
<!-- git clone https://github.com/shravelle/shravelle-dotnet, then: -->
<ProjectReference Include="../shravelle-dotnet/Shravelle/Shravelle.csproj" />
```
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddShravelle(o => o.ServiceName = "orders-api");   // SHRAVELLE_API_KEY / SHRAVELLE_ENDPOINT from env
```
Exports traces (ASP.NET Core, HttpClient, SqlClient), metrics (ASP.NET Core, HTTP, runtime/GC) and
`ILogger` logs. Options: `Endpoint`, `ApiKey`, `ServiceName`, `ServiceVersion`, `Environment`, `SampleRate`.
```csharp
ShravelleApi.NoticeError(ex);
ShravelleApi.SetUser(userId);
ShravelleApi.AddAttribute("cart_value", 1450);
ShravelleApi.RecordEvent("coupon_applied", new Dictionary<string, object?> { ["code"] = "DIWALI" });
using var act = ShravelleApi.Source.StartActivity("pricing.calculate");
ShravelleApi.Meter.CreateCounter<long>("orders_total").Add(1);
```
Entity Framework Core, gRPC, Redis and others: add the corresponding `OpenTelemetry.Instrumentation.*` packages.
Zero code alternative: the OpenTelemetry .NET automatic instrumentation with the `OTEL_*` variables.
Status: not compiled in this build environment.


---
Part of [Shravelle](https://shravelle.com). Setup guide for every platform: [shravelle.com/docs](https://shravelle.com/docs.html) · Get your keys: [shravelle.com/get-started](https://shravelle.com/get-started.html)
