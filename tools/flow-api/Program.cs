using System.Text.Json;
using System.Text.Json.Serialization;
using FlowApi.Endpoints;
using FlowCore.Storage;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var flowHome = Environment.GetEnvironmentVariable("FLOW_HOME")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".flow");

builder.Services.AddSingleton(new FlowStoreFactory(flowHome));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

// SPA static files (flow-web build output)
// dotnet run 시에는 flow-web/dist, publish 시에는 bin 아래 wwwroot
var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
var devWebRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "flow-web", "dist"));
var resolvedWebRoot = Directory.Exists(webRoot) && File.Exists(Path.Combine(webRoot, "index.html"))
    ? webRoot
    : Directory.Exists(devWebRoot) ? devWebRoot : null;

if (resolvedWebRoot != null)
{
    var fileProvider = new PhysicalFileProvider(resolvedWebRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
}

app.MapProjectEndpoints();
app.MapSpecEndpoints();
app.MapAssignmentEndpoints();
app.MapReviewEndpoints();
app.MapEventEndpoints();
app.MapValidationEndpoints();
app.MapActivityEndpoints();
app.MapEvidenceEndpoints();

// SPA fallback: non-API routes → index.html
if (resolvedWebRoot != null)
{
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(Path.Combine(resolvedWebRoot, "index.html"));
    });
}

app.Run();

// WebApplicationFactory 지원용
public partial class Program { }
