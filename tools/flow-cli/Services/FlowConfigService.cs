using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowCLI.Models;

namespace FlowCLI.Services;

/// <summary>
/// .flow/config.json 설정 파일을 읽고 쓰는 서비스.
/// specRepository 및 Runner 동작 설정을 관리한다.
/// </summary>
public class FlowConfigService
{
    private readonly string _configPath;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FlowConfigService(string configPath)
    {
        _configPath = configPath;
    }

    /// <summary>
    /// .flow/config.json을 로드한다.
    /// 파일이 없으면 기본값 FlowConfig를 반환한다.
    /// </summary>
    public FlowConfig Load()
    {
        if (!File.Exists(_configPath))
            return new FlowConfig();

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<FlowConfig>(json, ReadOptions) ?? new FlowConfig();
        }
        catch
        {
            return new FlowConfig();
        }
    }

    /// <summary>
    /// FlowConfig를 .flow/config.json에 저장한다.
    /// </summary>
    public void Save(FlowConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, WriteOptions);
        File.WriteAllText(_configPath, json);
    }

    /// <summary>
    /// specRepository를 읽어 반환한다.
    /// </summary>
    public string? GetSpecRepository() => Load().SpecRepository;

    /// <summary>
    /// specRepository URL을 config.json에 저장한다.
    /// </summary>
    public void SetSpecRepository(string url)
    {
        var config = Load();
        config.SpecRepository = url;
        Save(config);
    }
}
