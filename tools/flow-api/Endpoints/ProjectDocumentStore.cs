using System.Text.Json;
using FlowCore.Models;
using FlowCore.Serialization;

namespace FlowApi.Endpoints;

/// <summary>프로젝트 문서 JSON 파일 읽기/쓰기. 경로: {flowHome}/projects/{projectId}/project.json</summary>
public static class ProjectDocumentStore
{
    public static ProjectDocument? Load(string flowHome, string projectId)
    {
        var path = Path.Combine(flowHome, "projects", projectId, "project.json");
        if (!File.Exists(path))
            return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ProjectDocument>(json, FlowJsonOptions.Default);
    }

    /// <summary>저장. expectedVersion이 현재 문서 버전과 다르면 실패 (optimistic concurrency).</summary>
    public static (bool IsSuccess, string? Error) Save(string flowHome, string projectId, ProjectDocument doc, int expectedVersion)
    {
        var path = Path.Combine(flowHome, "projects", projectId, "project.json");
        var existing = Load(flowHome, projectId);
        var currentVersion = existing?.Version ?? 0;

        if (currentVersion != expectedVersion)
            return (false, $"version conflict: expected {expectedVersion}, current {currentVersion}");

        doc.Version = currentVersion + 1;
        doc.UpdatedAt = DateTimeOffset.UtcNow;

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(doc, FlowJsonOptions.Default);
        File.WriteAllText(path, json);
        return (true, null);
    }
}

/// <summary>에픽 문서 JSON 파일 읽기/쓰기. 경로: {flowHome}/projects/{projectId}/epics/{epicId}.json</summary>
public static class EpicDocumentStore
{
    public static EpicDocument? Load(string flowHome, string projectId, string epicId)
    {
        var path = Path.Combine(flowHome, "projects", projectId, "epics", $"{epicId}.json");
        if (!File.Exists(path))
            return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EpicDocument>(json, FlowJsonOptions.Default);
    }

    public static IReadOnlyList<EpicDocument> LoadAll(string flowHome, string projectId)
    {
        var dir = Path.Combine(flowHome, "projects", projectId, "epics");
        if (!Directory.Exists(dir))
            return [];

        var result = new List<EpicDocument>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var doc = JsonSerializer.Deserialize<EpicDocument>(json, FlowJsonOptions.Default);
                if (doc != null)
                    result.Add(doc);
            }
            catch
            {
                // skip malformed files
            }
        }
        return result;
    }

    /// <summary>저장. expectedVersion이 현재 문서 버전과 다르면 실패 (optimistic concurrency).</summary>
    public static (bool IsSuccess, string? Error) Save(string flowHome, string projectId, string epicId, EpicDocument doc, int expectedVersion)
    {
        var existing = Load(flowHome, projectId, epicId);
        var currentVersion = existing?.Version ?? 0;

        if (currentVersion != expectedVersion)
            return (false, $"version conflict: expected {expectedVersion}, current {currentVersion}");

        doc.Version = currentVersion + 1;
        doc.UpdatedAt = DateTimeOffset.UtcNow;

        var path = Path.Combine(flowHome, "projects", projectId, "epics", $"{epicId}.json");
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(doc, FlowJsonOptions.Default);
        File.WriteAllText(path, json);
        return (true, null);
    }
}
