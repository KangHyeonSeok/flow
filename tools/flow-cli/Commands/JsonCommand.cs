using System.Text.Json;
using Cocona;
using FlowCLI.Models;
using FlowCLI.Utils;

namespace FlowCLI;

public partial class FlowApp
{
    /// <summary>
    /// F-002-C1: JSON 요청 계약으로 임의 명령을 실행한다.
    /// stdin, 파일, 또는 직접 문자열로 FlowRequest JSON을 입력받아 대상 명령 핸들러로 라우팅한다.
    /// </summary>
    [Command("invoke", Description = "Execute a command via JSON request contract (stdin, file, or direct string)")]
    public void Invoke(
        [Argument(Description = "JSON request body (use --stdin or --file to read from other sources)")] string? json = null,
        [Option("stdin", Description = "Read JSON request from stdin")] bool stdin = false,
        [Option("file", Description = "Read JSON request from file path")] string? file = null,
        [Option("pretty", Description = "Pretty print JSON output")] bool pretty = false)
    {
        try
        {
            // 1. JSON 입력 소스 결정 (stdin > file > direct string)
            string rawJson;
            string inputSource;
            if (stdin)
            {
                rawJson = Console.In.ReadToEnd();
                inputSource = "stdin";
            }
            else if (!string.IsNullOrEmpty(file))
            {
                var filePath = Path.IsPathRooted(file) ? file : Path.Combine(Directory.GetCurrentDirectory(), file);
                if (!File.Exists(filePath))
                {
                    JsonOutput.Write(JsonOutput.Error("invoke",
                        $"요청 파일을 찾을 수 없습니다: {file}",
                        new { path = filePath }), pretty);
                    Environment.ExitCode = 1;
                    return;
                }
                rawJson = File.ReadAllText(filePath);
                inputSource = $"file:{file}";
            }
            else if (!string.IsNullOrEmpty(json))
            {
                rawJson = json;
                inputSource = "argument";
            }
            else
            {
                JsonOutput.Write(JsonOutput.Error("invoke",
                    "JSON 요청 입력이 필요합니다. 인자로 직접 전달하거나 --stdin 또는 --file 옵션을 사용하세요.",
                    new { usage = "flow invoke '<json>' | flow invoke --stdin | flow invoke --file request.json" }), pretty);
                Environment.ExitCode = 1;
                return;
            }

            // 2. FlowRequest 역직렬화
            FlowRequest request;
            try
            {
                request = JsonSerializer.Deserialize<FlowRequest>(rawJson, JsonOutput.Read)
                    ?? throw new JsonException("역직렬화 결과가 null입니다.");
            }
            catch (JsonException ex)
            {
                JsonOutput.Write(new CommandResult
                {
                    Success = false,
                    Command = "invoke",
                    Error = new ErrorInfo { Code = "SCHEMA_ERROR", Message = $"JSON 요청 스키마 오류: {ex.Message}" },
                    ExitCode = 1
                }, pretty);
                Environment.ExitCode = 1;
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Command))
            {
                JsonOutput.Write(new CommandResult
                {
                    Success = false,
                    Command = "invoke",
                    Error = new ErrorInfo { Code = "SCHEMA_ERROR", Message = "'command' 필드는 필수입니다." },
                    ExitCode = 1
                }, pretty);
                Environment.ExitCode = 1;
                return;
            }

            // 3. 명령 라우팅
            RouteRequest(request, pretty, inputSource);
        }
        catch (Exception ex)
        {
            JsonOutput.Write(JsonOutput.Error("invoke", ex.Message), pretty);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// FlowRequest를 파싱하여 기존 명령 핸들러로 라우팅한다. F-002-C1, C2.
    /// </summary>
    private void RouteRequest(FlowRequest request, bool pretty, string inputSource)
    {
        var opts = request.Options;
        var args = request.Arguments ?? [];

        // pretty 옵션은 요청 options에서도 읽을 수 있음
        pretty = pretty || GetOption(opts, "pretty", false);

        switch (request.Command.ToLowerInvariant())
        {
            case "build":
                Build(
                    target: args.Length > 0 ? args[0] : GetOption<string?>(opts, "target", null),
                    platform: GetOption(opts, "platform", "auto"),
                    lint: GetOption(opts, "lint", false),
                    build: GetOption(opts, "build", false),
                    test: GetOption(opts, "test", false),
                    run: GetOption(opts, "run", false),
                    all: GetOption(opts, "all", false),
                    config: GetOption<string?>(opts, "config", null),
                    timeout: GetOption(opts, "timeout", 300),
                    pretty: pretty);
                break;

            case "config":
                Config(
                    log: GetOption<string?>(opts, "log", null),
                    specRepo: GetOption<string?>(opts, "spec-repo", null),
                    pretty: pretty);
                break;

            case "db-add":
                DbAdd(
                    content: GetOption(opts, "content", ""),
                    tags: GetOption(opts, "tags", ""),
                    commitId: GetOption(opts, "commit-id", ""),
                    featureName: GetOption<string?>(opts, "feature", null),
                    planPath: GetOption<string?>(opts, "plan", null),
                    resultPath: GetOption<string?>(opts, "result", null),
                    pretty: pretty);
                break;

            case "db-query":
                DbQuery(
                    query: GetOption(opts, "query", ""),
                    plan: GetOption(opts, "plan", false),
                    result: GetOption(opts, "result", false),
                    tags: GetOption<string?>(opts, "tags", null),
                    top: GetOption(opts, "top", 5),
                    pretty: pretty);
                break;

            case "test":
                var testSubCmd = request.Subcommand ?? (args.Length > 0 ? args[0] : "");
                var testTarget = args.Length > 1 ? args[1] : (args.Length == 1 && request.Subcommand != null ? args[0] : GetOption<string?>(opts, "target", null));
                Test(
                    subCommand: testSubCmd,
                    target: testTarget,
                    timeout: GetOption(opts, "timeout", 300),
                    retry: GetOption(opts, "retry", 3),
                    platform: GetOption<string?>(opts, "platform", null),
                    saveReport: GetOption(opts, "save-report", false),
                    pretty: pretty);
                break;

            case "runner-start":
                RunnerStart(
                    daemon: GetOption(opts, "daemon", false),
                    interval: GetOption(opts, "interval", 0),
                    model: GetOption<string?>(opts, "model", null),
                    once: GetOption(opts, "once", false),
                    pretty: pretty);
                break;

            case "runner-status":
                RunnerStatus(pretty: pretty);
                break;

            case "runner-stop":
                RunnerStop(pretty: pretty);
                break;

            case "runner-logs":
                RunnerLogs(
                    tail: GetOption(opts, "tail", 50),
                    list: GetOption(opts, "list", false),
                    pretty: pretty);
                break;

            case "human-input":
                HumanInput(
                    type: GetOption(opts, "type", "text"),
                    prompt: GetOption(opts, "prompt", ""),
                    options: GetOption<string[]?>(opts, "options", null),
                    timeout: GetOptionNullable<int>(opts, "timeout"),
                    defaultValue: GetOption<string?>(opts, "default", null),
                    pretty: pretty);
                break;

            case "spec-init":
                SpecInit(pretty: pretty);
                break;

            case "spec-create":
                SpecCreate(
                    id: GetOption<string?>(opts, "id", null),
                    title: GetOption(opts, "title", ""),
                    description: GetOption<string?>(opts, "description", null),
                    parent: GetOption<string?>(opts, "parent", null),
                    status: GetOption(opts, "status", "draft"),
                    tags: GetOption<string?>(opts, "tags", null),
                    dependencies: GetOption<string?>(opts, "dependencies", null),
                    pretty: pretty);
                break;

            case "spec-get":
                var specGetId = args.Length > 0 ? args[0] : GetOption(opts, "id", "");
                SpecGet(
                    id: specGetId,
                    json: true,
                    pretty: pretty);
                break;

            case "spec-list":
                SpecList(
                    status: GetOption<string?>(opts, "status", null),
                    tag: GetOption<string?>(opts, "tag", null),
                    pretty: pretty);
                break;

            case "spec-delete":
                var specDelId = args.Length > 0 ? args[0] : GetOption(opts, "id", "");
                SpecDelete(id: specDelId, pretty: pretty);
                break;

            case "spec-validate":
                SpecValidate(
                    strict: GetOption(opts, "strict", false),
                    id: GetOption<string?>(opts, "id", null),
                    pretty: pretty);
                break;

            case "spec-graph":
                SpecGraph(
                    output: GetOption<string?>(opts, "output", null),
                    tree: GetOption(opts, "tree", false),
                    pretty: pretty);
                break;

            case "spec-impact":
                var impactId = args.Length > 0 ? args[0] : GetOption(opts, "id", "");
                SpecImpact(
                    id: impactId,
                    depth: GetOption(opts, "depth", 10),
                    pretty: pretty);
                break;

            case "spec-propagate":
                var propId = args.Length > 0 ? args[0] : GetOption(opts, "id", "");
                SpecPropagate(
                    id: propId,
                    status: GetOption(opts, "status", "needs-review"),
                    apply: GetOption(opts, "apply", false),
                    pretty: pretty);
                break;

            case "spec-check-refs":
                SpecCheckRefs(
                    strict: GetOption(opts, "strict", false),
                    pretty: pretty);
                break;

            case "spec-order":
                SpecOrder(
                    from: GetOption<string?>(opts, "from", null),
                    ai: GetOption(opts, "ai", false),
                    model: GetOption<string?>(opts, "model", null),
                    pretty: pretty);
                break;

            default:
                JsonOutput.Write(new CommandResult
                {
                    Success = false,
                    Command = "invoke",
                    Error = new ErrorInfo
                    {
                        Code = "UNKNOWN_COMMAND",
                        Message = $"알 수 없는 명령: '{request.Command}'",
                        Details = new
                        {
                            command = request.Command,
                            supported = new[]
                            {
                                "build", "config", "db-add", "db-query", "test",
                                "runner-start", "runner-status", "runner-stop", "runner-logs",
                                "human-input", "spec-init", "spec-create", "spec-get",
                                "spec-list", "spec-delete", "spec-validate", "spec-graph",
                                "spec-impact", "spec-propagate", "spec-check-refs", "spec-order"
                            }
                        }
                    },
                    ExitCode = 1
                }, pretty);
                Environment.ExitCode = 1;
                break;
        }
    }

    // ─── 옵션 추출 헬퍼 ─────────────────────────────────────────────────

    /// <summary>FlowRequest.Options 딕셔너리에서 typed 값을 안전하게 추출한다.</summary>
    private static T GetOption<T>(Dictionary<string, JsonElement>? options, string key, T defaultValue)
    {
        if (options == null || !options.TryGetValue(key, out var element))
            return defaultValue;

        try
        {
            var result = element.Deserialize<T>(JsonOutput.Read);
            return result ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>Nullable 값 타입을 위한 옵션 추출 헬퍼.</summary>
    private static T? GetOptionNullable<T>(Dictionary<string, JsonElement>? options, string key) where T : struct
    {
        if (options == null || !options.TryGetValue(key, out var element))
            return null;

        try
        {
            return element.Deserialize<T>(JsonOutput.Read);
        }
        catch
        {
            return null;
        }
    }
}
