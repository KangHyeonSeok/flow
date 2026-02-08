using System.Text.Json.Serialization;
using EmbedCLI.Models;
using EmbedCLI.Services;

namespace EmbedCLI;

[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(EmbedStatusResponse))]
[JsonSourceGenerationOptions(WriteIndented = false)]
public partial class EmbedJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(CacheData))]
[JsonSerializable(typeof(CacheEntry))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class CacheJsonContext : JsonSerializerContext
{}