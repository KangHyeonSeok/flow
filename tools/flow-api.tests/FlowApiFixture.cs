using System.Text.Json;
using System.Text.Json.Serialization;
using FlowCore.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace flow_api.tests;

/// <summary>
/// WebApplicationFactory fixture that redirects FLOW_HOME to a temp directory.
/// Shared across tests in the same collection for performance.
/// </summary>
public sealed class FlowApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private string? _tempDir;

    public HttpClient Client { get; private set; } = null!;
    public string FlowHome => _tempDir!;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public Task InitializeAsync()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flow-api-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Disable static web assets (avoids wwwroot not found error)
                builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Production");

                builder.ConfigureServices(services =>
                {
                    // Replace FlowStoreFactory with one pointing to temp dir
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(FlowStoreFactory));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    services.AddSingleton(new FlowStoreFactory(_tempDir));
                });
            });

        Client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();

        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); }
            catch { /* best effort cleanup */ }
        }
        return Task.CompletedTask;
    }
}
