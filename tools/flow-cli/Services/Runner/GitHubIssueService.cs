using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FlowCLI.Services.SpecGraph;

namespace FlowCLI.Services.Runner;

/// <summary>
/// GitHub 이슈를 주기적으로 확인하여 스펙과 연결·생성하는 서비스.
/// (F-070-C11~C15)
/// </summary>
public class GitHubIssueService
{
    private readonly RunnerConfig _config;
    private readonly SpecStore _specStore;
    private readonly CopilotService _copilot;
    private readonly RunnerLogService _log;
    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;

    /// <summary>마지막으로 이슈를 확인한 시각 (ISO 8601)</summary>
    private DateTime _lastCheckedAt;
    private readonly string _stateFilePath;

    private static readonly Regex SpecIdPattern = new(@"F-\d{3}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public GitHubIssueService(
        RunnerConfig config,
        SpecStore specStore,
        CopilotService copilot,
        RunnerLogService log,
        string flowRoot)
    {
        _config = config;
        _specStore = specStore;
        _copilot = copilot;
        _log = log;

        // GitHub repo 파싱 (owner/repo)
        var (owner, repo) = ParseGitHubRepo(config.GitHubRepo);
        _owner = owner;
        _repo = repo;

        // GitHub API 클라이언트 설정
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FlowRunner", "1.0"));

        // 토큰 설정 (환경변수 우선)
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? config.GitHubToken;
        if (!string.IsNullOrEmpty(token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // 마지막 확인 시각 상태 파일
        _stateFilePath = Path.Combine(flowRoot, "issue-check-state.json");
        _lastCheckedAt = LoadLastCheckedAt();
    }

    /// <summary>
    /// 테스트용 생성자: HttpClient를 주입할 수 있다.
    /// </summary>
    internal GitHubIssueService(
        RunnerConfig config,
        SpecStore specStore,
        CopilotService copilot,
        RunnerLogService log,
        string flowRoot,
        HttpClient httpClient)
    {
        _config = config;
        _specStore = specStore;
        _copilot = copilot;
        _log = log;

        var (owner, repo) = ParseGitHubRepo(config.GitHubRepo);
        _owner = owner;
        _repo = repo;
        _http = httpClient;

        _stateFilePath = Path.Combine(flowRoot, "issue-check-state.json");
        _lastCheckedAt = LoadLastCheckedAt();
    }

    /// <summary>
    /// GitHub 이슈를 확인하고 스펙과 연결/생성한다.
    /// (F-070-C11: 오픈 이슈 조회, 마지막 확인 시각 이후 변경 감지)
    /// </summary>
    public async Task<List<IssueProcessResult>> ProcessIssuesAsync()
    {
        var results = new List<IssueProcessResult>();

        _log.Info("github-issues", $"GitHub 이슈 확인 시작 (since: {_lastCheckedAt:o})");

        try
        {
            // C11: GitHub API로 오픈 이슈 목록 조회
            var issues = await FetchOpenIssuesAsync(_lastCheckedAt);
            if (issues.Count == 0)
            {
                _log.Info("github-issues", "새로운/변경된 이슈 없음");
                SaveLastCheckedAt(DateTime.UtcNow);
                return results;
            }

            _log.Info("github-issues", $"처리 대상 이슈 {issues.Count}개 발견");

            foreach (var issue in issues)
            {
                var result = await ProcessSingleIssueAsync(issue);
                results.Add(result);
            }

            // 체크 시각 업데이트
            SaveLastCheckedAt(DateTime.UtcNow);

            _log.Info("github-issues",
                $"GitHub 이슈 처리 완료: {results.Count(r => r.Success)} 성공 / {results.Count(r => !r.Success)} 실패");
        }
        catch (Exception ex)
        {
            _log.Error("github-issues", $"GitHub 이슈 처리 중 오류: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// 단일 이슈를 처리한다.
    /// </summary>
    private async Task<IssueProcessResult> ProcessSingleIssueAsync(GitHubIssueInfo issue)
    {
        var result = new IssueProcessResult
        {
            IssueNumber = issue.Number,
            IssueTitle = issue.Title
        };

        try
        {
            // 이미 spec-linked 라벨이 있으면 스킵
            if (issue.Labels.Contains(_config.SpecLinkLabel))
            {
                result.Action = "skipped";
                result.Success = true;
                _log.Info("github-issues", $"이미 연결된 이슈, 스킵: #{issue.Number}", $"issue-{issue.Number}");
                return result;
            }

            // C12: 이슈 본문/라벨에서 스펙 ID 참조 확인
            var referencedSpecIds = FindSpecReferences(issue);
            if (referencedSpecIds.Count > 0)
            {
                // 기존 스펙 참조 발견 → 연결
                var specId = referencedSpecIds.First();
                var spec = _specStore.Get(specId);
                if (spec != null)
                {
                    await LinkIssueToSpecAsync(issue, specId);
                    UpdateSpecGitHubIssues(spec, issue.Number);

                    result.Action = "linked";
                    result.SpecId = specId;
                    result.Success = true;

                    // C15: 처리 내역 로깅
                    _log.Info("github-issues",
                        $"이슈 #{issue.Number} → 스펙 {specId} 연결 완료", specId);
                    return result;
                }
            }

            // C13: 이슈 제목/본문으로 유사 스펙 검색 (키워드 매칭)
            var relatedSpec = FindRelatedSpec(issue);
            if (relatedSpec != null)
            {
                await LinkIssueToSpecAsync(issue, relatedSpec.Id);
                UpdateSpecGitHubIssues(relatedSpec, issue.Number);

                result.Action = "linked";
                result.SpecId = relatedSpec.Id;
                result.Success = true;

                _log.Info("github-issues",
                    $"이슈 #{issue.Number} → 유사 스펙 {relatedSpec.Id} 연결 완료 (키워드 매칭)", relatedSpec.Id);
                return result;
            }

            // C14: 관련 스펙이 없으면 새 스펙 생성
            var newSpec = await CreateSpecFromIssueAsync(issue);
            if (newSpec != null)
            {
                await LinkIssueToSpecAsync(issue, newSpec.Id, isAutoCreated: true);
                await NotifySpecCreatedAsync(issue, newSpec.Id);

                result.Action = "created";
                result.SpecId = newSpec.Id;
                result.Success = true;

                _log.Info("github-issues",
                    $"이슈 #{issue.Number} → 새 스펙 {newSpec.Id} 생성 및 연결 완료", newSpec.Id);
                return result;
            }

            result.Action = "error";
            result.Success = false;
            result.ErrorMessage = "스펙 생성 실패";
            _log.Error("github-issues", $"이슈 #{issue.Number} 처리 실패: 스펙 생성 불가");
        }
        catch (Exception ex)
        {
            result.Action = "error";
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _log.Error("github-issues", $"이슈 #{issue.Number} 처리 중 오류: {ex.Message}");
        }

        return result;
    }

    // ── GitHub API 호출 ────────────────────────────────────────

    /// <summary>
    /// C11: GitHub API로 오픈 이슈 목록을 조회한다.
    /// since 이후 변경된 이슈만 가져온다.
    /// </summary>
    internal async Task<List<GitHubIssueInfo>> FetchOpenIssuesAsync(DateTime since)
    {
        var sinceStr = since.ToString("o");
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/issues?state=open&since={sinceStr}&per_page=100&sort=updated&direction=desc";

        var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _log.Error("github-api", $"GitHub API 요청 실패: {response.StatusCode} - {body}");
            return new List<GitHubIssueInfo>();
        }

        var json = await response.Content.ReadAsStringAsync();
        var rawIssues = JsonSerializer.Deserialize<List<GitHubRawIssue>>(json, JsonOpts);
        if (rawIssues == null) return new List<GitHubIssueInfo>();

        // Pull Request 제외 (GitHub Issues API는 PR도 반환)
        return rawIssues
            .Where(i => i.PullRequest == null)
            .Select(i => new GitHubIssueInfo
            {
                Number = i.Number,
                Title = i.Title ?? "",
                Body = i.Body ?? "",
                Labels = i.Labels?.Select(l => l.Name ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList()
                         ?? new List<string>(),
                State = i.State ?? "open",
                CreatedAt = i.CreatedAt ?? "",
                UpdatedAt = i.UpdatedAt ?? ""
            })
            .ToList();
    }

    /// <summary>
    /// C12: 이슈에 스펙 연결 댓글 + 라벨을 추가한다.
    /// </summary>
    private async Task LinkIssueToSpecAsync(GitHubIssueInfo issue, string specId, bool isAutoCreated = false)
    {
        // 댓글 추가
        var comment = _config.SpecLinkCommentTemplate.Replace("{specId}", specId);
        await PostCommentAsync(issue.Number, comment);

        // 라벨 추가
        var labels = new List<string> { _config.SpecLinkLabel };
        if (isAutoCreated)
            labels.Add(_config.AutoCreateSpecLabel);

        await AddLabelsAsync(issue.Number, labels);
    }

    /// <summary>
    /// C14: 새 스펙 생성 시 이슈에 알림 댓글을 게시한다.
    /// </summary>
    private async Task NotifySpecCreatedAsync(GitHubIssueInfo issue, string specId)
    {
        var message = $"🤖 새 스펙이 자동 생성되었습니다: **{specId}**\n\n" +
                      $"이 이슈를 기반으로 기능 스펙이 초안(draft) 상태로 생성되었습니다.\n" +
                      $"스펙 내용을 검토하고 필요 시 수정해 주세요.";
        await PostCommentAsync(issue.Number, message);
    }

    /// <summary>이슈에 댓글 추가</summary>
    private async Task PostCommentAsync(int issueNumber, string body)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_owner}/{_repo}/issues/{issueNumber}/comments";
            var payload = JsonSerializer.Serialize(new { body });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var respBody = await response.Content.ReadAsStringAsync();
                _log.Warn("github-api", $"댓글 추가 실패 (#{issueNumber}): {response.StatusCode} - {respBody}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn("github-api", $"댓글 추가 예외 (#{issueNumber}): {ex.Message}");
        }
    }

    /// <summary>이슈에 라벨 추가</summary>
    private async Task AddLabelsAsync(int issueNumber, List<string> labels)
    {
        try
        {
            var url = $"https://api.github.com/repos/{_owner}/{_repo}/issues/{issueNumber}/labels";
            var payload = JsonSerializer.Serialize(new { labels });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var respBody = await response.Content.ReadAsStringAsync();
                _log.Warn("github-api", $"라벨 추가 실패 (#{issueNumber}): {response.StatusCode} - {respBody}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn("github-api", $"라벨 추가 예외 (#{issueNumber}): {ex.Message}");
        }
    }

    // ── 스펙 매칭 / 생성 ──────────────────────────────────────

    /// <summary>
    /// C12: 이슈 제목/본문/라벨에서 F-xxx 패턴의 스펙 ID 참조를 찾는다.
    /// </summary>
    internal List<string> FindSpecReferences(GitHubIssueInfo issue)
    {
        var refs = new HashSet<string>();

        // 제목, 본문 검색
        foreach (Match m in SpecIdPattern.Matches(issue.Title))
            refs.Add(m.Value);
        foreach (Match m in SpecIdPattern.Matches(issue.Body))
            refs.Add(m.Value);

        // 라벨 검색
        foreach (var label in issue.Labels)
        {
            foreach (Match m in SpecIdPattern.Matches(label))
                refs.Add(m.Value);
        }

        // 존재하는 스펙만 반환
        return refs.Where(id => _specStore.Exists(id)).ToList();
    }

    /// <summary>
    /// C13: 이슈 제목/본문을 기반으로 유사 스펙을 키워드 매칭으로 검색한다.
    /// </summary>
    internal SpecNode? FindRelatedSpec(GitHubIssueInfo issue)
    {
        var allSpecs = _specStore.GetAll();
        if (allSpecs.Count == 0) return null;

        // 이슈 텍스트에서 키워드 추출 (공백으로 분리, 2자 이상)
        var issueText = $"{issue.Title} {issue.Body}".ToLowerInvariant();
        var issueWords = ExtractKeywords(issueText);

        if (issueWords.Count == 0) return null;

        // 각 스펙과의 유사도 계산 (Jaccard 유사도 기반)
        SpecNode? bestMatch = null;
        double bestScore = 0;

        foreach (var spec in allSpecs)
        {
            var specText = $"{spec.Title} {spec.Description} {string.Join(" ", spec.Tags)}".ToLowerInvariant();
            var specWords = ExtractKeywords(specText);

            if (specWords.Count == 0) continue;

            // Jaccard 유사도: 교집합 / 합집합
            var intersection = issueWords.Intersect(specWords).Count();
            var union = issueWords.Union(specWords).Count();

            if (union == 0) continue;

            var score = (double)intersection / union;

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = spec;
            }
        }

        // 임계값 이상이면 매칭 (0.15 이상)
        const double threshold = 0.15;
        if (bestScore >= threshold && bestMatch != null)
        {
            _log.Info("github-issues",
                $"이슈 #{issue.Number}과 유사 스펙 발견: {bestMatch.Id} (score: {bestScore:F3})");
            return bestMatch;
        }

        return null;
    }

    /// <summary>
    /// C14: 이슈를 기반으로 새 스펙을 생성한다.
    /// Copilot을 사용하여 스펙 JSON 초안을 생성.
    /// </summary>
    internal async Task<SpecNode?> CreateSpecFromIssueAsync(GitHubIssueInfo issue)
    {
        try
        {
            var newId = _specStore.NextId();

            // 이슈 정보에서 기본 스펙 생성
            var spec = new SpecNode
            {
                SchemaVersion = 2,
                Id = newId,
                NodeType = "feature",
                Title = issue.Title,
                Description = $"GitHub 이슈 #{issue.Number}에서 자동 생성.\n\n{TruncateBody(issue.Body, 500)}",
                Status = "draft",
                Tags = new List<string>(issue.Labels) { "auto-created", "github-issue" },
                Metadata = new Dictionary<string, object>
                {
                    ["githubIssues"] = new List<object> { issue.Number },
                    ["sourceIssue"] = issue.Number,
                    ["createdByRunner"] = true
                },
                Conditions = new List<SpecCondition>()
            };

            // Copilot으로 conditions 생성 시도 (선택적 - 실패 시 빈 조건으로 생성)
            try
            {
                var conditions = await GenerateConditionsAsync(issue, newId);
                if (conditions.Count > 0)
                {
                    spec.Conditions = conditions;
                }
            }
            catch (Exception ex)
            {
                _log.Warn("github-issues",
                    $"Copilot conditions 생성 실패, 빈 조건으로 진행: {ex.Message}");
            }

            _specStore.Create(spec);
            _log.Info("github-issues", $"새 스펙 생성 완료: {newId} (이슈 #{issue.Number})", newId);

            return spec;
        }
        catch (Exception ex)
        {
            _log.Error("github-issues", $"스펙 생성 실패 (이슈 #{issue.Number}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Copilot을 호출하여 이슈에서 수락 조건(conditions)을 생성한다.
    /// </summary>
    private async Task<List<SpecCondition>> GenerateConditionsAsync(GitHubIssueInfo issue, string specId)
    {
        var prompt = $$"""
            다음 GitHub 이슈를 분석하여 Given-When-Then 형식의 수락 조건을 JSON 배열로 생성하세요.
            출력은 JSON 배열만 반환하고 다른 텍스트는 포함하지 마세요.

            이슈 제목: {{issue.Title}}
            이슈 본문: {{TruncateBody(issue.Body, 1000)}}

            출력 형식:
            [
              {"id": "{{specId}}-C1", "nodeType": "condition", "description": "Given ... When ... Then ...", "status": "draft", "codeRefs": [], "evidence": []}
            ]
            """;

        var result = await _copilot.ImplementSpecAsync(specId, prompt, Directory.GetCurrentDirectory());

        if (result.Success && !string.IsNullOrEmpty(result.Output))
        {
            // 출력에서 JSON 배열 추출 시도
            var jsonMatch = Regex.Match(result.Output, @"\[[\s\S]*\]");
            if (jsonMatch.Success)
            {
                try
                {
                    var conditions = JsonSerializer.Deserialize<List<SpecCondition>>(jsonMatch.Value, JsonOpts);
                    return conditions ?? new List<SpecCondition>();
                }
                catch
                {
                    _log.Warn("github-issues", "Copilot 출력 파싱 실패");
                }
            }
        }

        return new List<SpecCondition>();
    }

    /// <summary>
    /// C15: 스펙의 metadata.githubIssues 배열에 이슈 번호를 추가한다.
    /// </summary>
    private void UpdateSpecGitHubIssues(SpecNode spec, int issueNumber)
    {
        spec.Metadata ??= new Dictionary<string, object>();

        List<object> githubIssues;
        if (spec.Metadata.TryGetValue("githubIssues", out var existing) && existing is JsonElement element)
        {
            githubIssues = element.EnumerateArray()
                .Select(e => (object)e.GetInt32())
                .ToList();
        }
        else if (existing is List<object> list)
        {
            githubIssues = list;
        }
        else
        {
            githubIssues = new List<object>();
        }

        if (!githubIssues.Any(n => n.ToString() == issueNumber.ToString()))
        {
            githubIssues.Add(issueNumber);
            spec.Metadata["githubIssues"] = githubIssues;
            _specStore.Update(spec);
        }
    }

    // ── F-015: 큐 이슈 연결 동기화 및 우선순위 점수 ─────────────

    /// <summary>
    /// F-015-C1: queued 스펙 배치의 이슈 연결 상태를 동기화하고 우선순위 점수를 계산한다.
    /// 현재 오픈 이슈 스냅샷을 획득한 뒤, 각 스펙의 githubIssues를 재검증(C5)하고
    /// metadata.queuePriority / issuePriorityScore를 갱신한다(C2).
    /// </summary>
    public async Task SyncQueuedSpecIssueConnectionsAsync(List<SpecNode> queuedSpecs)
    {
        if (queuedSpecs.Count == 0) return;

        _log.Info("queue-priority", $"queued 스펙 {queuedSpecs.Count}개 이슈 연결 동기화 시작 (F-015-C1)");

        // C1: 현재 오픈 이슈 전체 스냅샷 획득
        List<GitHubIssueInfo> openIssues;
        try
        {
            openIssues = await FetchAllOpenIssuesAsync();
            _log.Info("queue-priority", $"오픈 이슈 {openIssues.Count}개 스냅샷 획득 (lastRefreshedAt: {DateTime.UtcNow:o})");
        }
        catch (Exception ex)
        {
            _log.Warn("queue-priority", $"이슈 스냅샷 획득 실패, 동기화 건너뜀: {ex.Message}");
            return;
        }

        var openIssueSet = openIssues.ToDictionary(i => i.Number);

        foreach (var spec in queuedSpecs)
        {
            spec.Metadata ??= new Dictionary<string, object>();

            // C5: 기존 linkedIssues 재검증 — 닫히거나 연결이 끊긴 이슈 제거
            var linkedNums = GetLinkedIssueNumbers(spec);
            var validNums = linkedNums.Where(n => openIssueSet.ContainsKey(n)).ToList();
            var staleNums = linkedNums.Except(validNums).ToList();

            if (staleNums.Count > 0)
            {
                _log.Info("queue-priority",
                    $"스펙 {spec.Id}: stale 이슈 제거 ({string.Join(", ", staleNums.Select(n => $"#{n}"))}) — closed/disconnected", spec.Id);
            }

            // C5: 새로 spec ID를 참조하는 오픈 이슈 스캔
            foreach (var issue in openIssues)
            {
                if (validNums.Contains(issue.Number)) continue;
                if (issue.Title.Contains(spec.Id) || issue.Body.Contains(spec.Id) ||
                    issue.Labels.Any(l => l.Contains(spec.Id)))
                {
                    validNums.Add(issue.Number);
                    _log.Info("queue-priority",
                        $"스펙 {spec.Id}: 이슈 #{issue.Number} 신규 연결 발견", spec.Id);
                }
            }

            // githubIssues 메타데이터 갱신
            spec.Metadata["githubIssues"] = validNums.Cast<object>().ToList();

            // C2: 우선순위 점수 계산 — 연결된 오픈 이슈 리스트 기반
            var linkedOpenIssues = validNums
                .Where(n => openIssueSet.ContainsKey(n))
                .Select(n => openIssueSet[n])
                .ToList();

            var priority = CalculateIssuePriorityScore(spec, linkedOpenIssues);
            spec.Metadata["issuePriorityScore"] = priority.Score;
            spec.Metadata["queuePriority"] = priority;

            _specStore.Update(spec);

            if (priority.Score > 0)
                _log.Info("queue-priority",
                    $"스펙 {spec.Id} 우선순위 점수: {priority.Score:F1} [{string.Join("; ", priority.Reasons)}]", spec.Id);
        }
    }

    /// <summary>
    /// F-015-C2: 스펙과 연결된 오픈 이슈들을 기반으로 우선순위 점수를 계산한다.
    /// 직접 참조 여부, 오픈 이슈 수, 최근성, 우선순위 라벨, 자동 생성 여부를 신호로 사용.
    /// </summary>
    public QueuePriorityInfo CalculateIssuePriorityScore(SpecNode spec, List<GitHubIssueInfo> linkedOpenIssues)
    {
        var reasons = new List<string>();
        double score = 0;

        // Signal 1: explicit-spec-reference — 이슈 본문/제목/라벨에 스펙 ID가 직접 언급됨
        var hasExplicitRef = linkedOpenIssues.Any(i =>
            i.Title.Contains(spec.Id, StringComparison.OrdinalIgnoreCase) ||
            i.Body.Contains(spec.Id, StringComparison.OrdinalIgnoreCase) ||
            i.Labels.Any(l => l.Contains(spec.Id, StringComparison.OrdinalIgnoreCase)));
        if (hasExplicitRef)
        {
            score += 50;
            reasons.Add("explicit-spec-reference(+50)");
        }

        // Signal 2: linked-open-issue-count — 연결된 오픈 이슈 수 (최대 +30)
        var openCount = linkedOpenIssues.Count;
        if (openCount > 0)
        {
            var countBonus = Math.Min(openCount * 10, 30);
            score += countBonus;
            reasons.Add($"linked-open-issues={openCount}(+{countBonus})");
        }

        // Signal 3: issue-recency — 가장 최근 이슈의 updatedAt 기준 최신성 보너스
        if (linkedOpenIssues.Count > 0)
        {
            var mostRecentUpdated = linkedOpenIssues
                .Where(i => DateTime.TryParse(i.UpdatedAt, out _))
                .Select(i => DateTime.Parse(i.UpdatedAt).ToUniversalTime())
                .OrderDescending()
                .FirstOrDefault();

            if (mostRecentUpdated != default)
            {
                var ageDays = (DateTime.UtcNow - mostRecentUpdated).TotalDays;
                double recencyBonus = ageDays switch
                {
                    < 1 => 20,
                    < 7 => 15,
                    < 30 => 10,
                    < 90 => 5,
                    _ => 0
                };
                if (recencyBonus > 0)
                {
                    score += recencyBonus;
                    reasons.Add($"issue-recency={ageDays:F0}d(+{recencyBonus})");
                }
            }
        }

        // Signal 4: priority-label — priority/bug/regression/hotfix/critical/urgent/p0/p1 계열 라벨
        var priorityLabelKeywords = new[] { "priority", "bug", "regression", "hotfix", "critical", "urgent", "p0", "p1" };
        var hasPriorityLabel = linkedOpenIssues.Any(i =>
            i.Labels.Any(l => priorityLabelKeywords.Any(kw =>
                l.ToLowerInvariant().Contains(kw))));
        if (hasPriorityLabel)
        {
            score += 20;
            reasons.Add("priority-label(+20)");
        }

        // Signal 5: auto-created-spec — 이슈에서 자동 생성된 스펙 (이슈 주도 스펙)
        var isAutoCreated = spec.Tags.Contains("auto-created") ||
            (spec.Metadata?.ContainsKey("createdByRunner") == true) ||
            (spec.Metadata?.ContainsKey("sourceIssue") == true && openCount > 0);
        if (isAutoCreated && openCount > 0)
        {
            score += 10;
            reasons.Add("auto-created-spec(+10)");
        }

        return new QueuePriorityInfo
        {
            Score = score,
            Reasons = reasons,
            LastRefreshedAt = DateTime.UtcNow.ToString("o")
        };
    }

    /// <summary>
    /// 스펙 metadata에서 연결된 이슈 번호 목록을 추출한다 (githubIssues + sourceIssue).
    /// </summary>
    internal static List<int> GetLinkedIssueNumbers(SpecNode spec)
    {
        var result = new List<int>();
        if (spec.Metadata == null) return result;

        // metadata.githubIssues 배열에서 추출
        if (spec.Metadata.TryGetValue("githubIssues", out var issuesVal))
        {
            if (issuesVal is System.Text.Json.JsonElement je &&
                je.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in je.EnumerateArray())
                {
                    if (el.TryGetInt32(out var n)) result.Add(n);
                }
            }
            else if (issuesVal is List<object> list)
            {
                foreach (var item in list)
                {
                    if (int.TryParse(item?.ToString(), out var n)) result.Add(n);
                }
            }
        }

        // metadata.sourceIssue (자동 생성 스펙)
        if (spec.Metadata.TryGetValue("sourceIssue", out var srcVal))
        {
            int num = -1;
            if (srcVal is System.Text.Json.JsonElement srcJe && srcJe.TryGetInt32(out var srcN))
                num = srcN;
            else
                int.TryParse(srcVal?.ToString(), out num);

            if (num > 0 && !result.Contains(num)) result.Add(num);
        }

        return result.Distinct().ToList();
    }

    /// <summary>
    /// 모든 오픈 이슈를 조회한다 (since 필터 없이 전체 스냅샷).
    /// </summary>
    internal Task<List<GitHubIssueInfo>> FetchAllOpenIssuesAsync()
        => FetchOpenIssuesAsync(DateTime.UtcNow.AddYears(-10));

    // ── 유틸리티 ───────────────────────────────────────────────

    /// <summary>텍스트에서 키워드를 추출한다 (공백/특수문자 분리, 2자 이상)</summary>
    internal static HashSet<string> ExtractKeywords(string text)
    {
        var words = Regex.Split(text, @"[\s\p{P}\p{S}]+")
            .Where(w => w.Length >= 2)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        // 불용어 제거
        var stopWords = new HashSet<string>
        {
            "the", "and", "for", "that", "this", "with", "from", "are", "was", "will",
            "have", "has", "been", "not", "but", "can", "should", "when", "then", "given",
            "하는", "있는", "하고", "한다", "이다", "있다", "없다", "위한", "대한", "통해"
        };

        words.ExceptWith(stopWords);
        return words;
    }

    /// <summary>본문을 maxLength 이하로 자른다.</summary>
    private static string TruncateBody(string body, int maxLength)
    {
        if (string.IsNullOrEmpty(body)) return "";
        return body.Length <= maxLength ? body : body[..maxLength] + "...";
    }

    /// <summary>
    /// githubRepo 설정에서 owner/repo를 파싱한다.
    /// </summary>
    internal static (string Owner, string Repo) ParseGitHubRepo(string? githubRepo)
    {
        if (!string.IsNullOrEmpty(githubRepo) && githubRepo.Contains('/'))
        {
            var parts = githubRepo.Split('/');
            return (parts[0], parts[1]);
        }

        throw new InvalidOperationException(
            "GitHub 저장소 정보를 확인할 수 없습니다. " +
            "config.json에 githubRepo(owner/repo 형식)를 설정하세요.");
    }

    /// <summary>마지막 이슈 확인 시각 로드</summary>
    private DateTime LoadLastCheckedAt()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<IssueCheckState>(json, JsonOpts);
                if (state != null && DateTime.TryParse(state.LastCheckedAt, out var dt))
                    return dt;
            }
        }
        catch { }

        // 기본값: 7일 전
        return DateTime.UtcNow.AddDays(-7);
    }

    /// <summary>마지막 이슈 확인 시각 저장</summary>
    private void SaveLastCheckedAt(DateTime dt)
    {
        try
        {
            _lastCheckedAt = dt;
            var state = new IssueCheckState { LastCheckedAt = dt.ToString("o") };
            var dir = Path.GetDirectoryName(_stateFilePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, JsonOpts));
        }
        catch (Exception ex)
        {
            _log.Warn("github-issues", $"이슈 체크 상태 저장 실패: {ex.Message}");
        }
    }

    // ── 내부 모델 ──────────────────────────────────────────────

    private class IssueCheckState
    {
        [JsonPropertyName("lastCheckedAt")]
        public string LastCheckedAt { get; set; } = "";
    }

    /// <summary>GitHub API 원시 이슈 모델</summary>
    internal class GitHubRawIssue
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("labels")]
        public List<GitHubRawLabel>? Labels { get; set; }

        [JsonPropertyName("pull_request")]
        public object? PullRequest { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; set; }
    }

    internal class GitHubRawLabel
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
