using System.Diagnostics;
using System.Text.Json;

namespace FlowCLI.Services;

/// <summary>
/// Bridge to embed.exe for generating text embeddings.
/// Calls embed.exe as a subprocess and returns the embedding vector.
/// </summary>
public class EmbeddingBridge
{
    private readonly PathResolver _paths;

    public EmbeddingBridge(PathResolver paths) => _paths = paths;

    /// <summary>
    /// Generate an embedding vector for the given text by calling embed.exe.
    /// Returns null if embed.exe is not found or the process fails.
    /// </summary>
    public async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        var embedExe = _paths.EmbedExePath;
        if (!File.Exists(embedExe))
            return null;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = embedExe,
                Arguments = $"embed \"{text.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize<float[]>(output.Trim());
        }
        catch
        {
            // fall through to status handling
        }

        try
        {
            using var doc = JsonDocument.Parse(output.Trim());
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("status", out var status) &&
                string.Equals(status.GetString(), "preparing", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        catch
        {
            // ignore non-status payloads
        }

        return null;
    }
}
