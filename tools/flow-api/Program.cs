using System.Text.Json;
using System.Text.Json.Serialization;
using FlowApi.Endpoints;
using FlowCore.Runner;
using FlowCore.Storage;

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

app.MapProjectEndpoints();
app.MapSpecEndpoints();
app.MapAssignmentEndpoints();
app.MapReviewEndpoints();
app.MapEventEndpoints();
app.MapActivityEndpoints();
app.MapEvidenceEndpoints();

app.Run();

// WebApplicationFactory 지원용
public partial class Program { }
