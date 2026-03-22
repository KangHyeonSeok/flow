using System.Text.Json;
using FlowCore.Models;
using FlowCore.Serialization;

namespace FlowApi.Endpoints;

/// <summary>프로젝트 문서 JSON 파일 읽기. 경로: {flowHome}/projects/{projectId}/project.json</summary>
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
}

/// <summary>에픽 문서 JSON 파일 읽기. 경로: {flowHome}/projects/{projectId}/epics/{epicId}.json</summary>
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
}
