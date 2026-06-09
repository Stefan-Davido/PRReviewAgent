using Anthropic;
using Anthropic.Models.Messages;
using PRReviewAgent.Models;
using System.Text.Json;

namespace PRReviewAgent.Services;

public class PRReviewService
{
    private readonly AnthropicClient _claude;
    private readonly AzureDevOpsClient _ado;
    private readonly string _skillContent;
    private readonly ILogger<PRReviewService> _logger;

    public PRReviewService(IConfiguration config, ILogger<PRReviewService> logger,
        ILogger<AzureDevOpsClient> adoLogger)
    {
        _logger = logger;

        var apikey = config["Anthropic:ApiKey"];

        _claude = new() { ApiKey = apikey };

        //_claude = new AnthropicClient(new Anthropic.Core.ClientOptions()) ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured.");

        _ado = new AzureDevOpsClient(
            config["AzureDevOps:Token"]
                ?? throw new InvalidOperationException("AzureDevOps:Token is not configured."),
            config["AzureDevOps:OrgUrl"]
                ?? throw new InvalidOperationException("AzureDevOps:OrgUrl is not configured."),
            adoLogger
        );

        // Skill = system prompt loaded from file at startup
        var skillPath = Path.Combine(AppContext.BaseDirectory, "skills", "pr-reviewer", "SKILL.md");
        if (!File.Exists(skillPath))
            throw new FileNotFoundException($"Skill file not found at: {skillPath}");

        _skillContent = File.ReadAllText(skillPath);
        _logger.LogInformation("Skill loaded from {Path} ({Length} chars)", skillPath, _skillContent.Length);
    }

    public async Task<PRVerdict> ReviewAsync(string project, int prId)
    {
        _logger.LogInformation("Starting review for PR #{PrId} in project '{Project}'", prId, project);

        // 1. Fetch PR data from Azure DevOps
        var pr = await _ado.GetPullRequestAsync(project, prId);

       // var json = await response.Content.ReadAsStringAsync();
       var itterationList = await _ado.GetPRDiffAsync(project, prId);
        var diff = await _ado.GetPRDiffAsync(project, prId);

        var result = JsonSerializer.Deserialize<JsonElement>(itterationList);

        var iterations = result.GetProperty("value");
        var latestIteration = iterations[iterations.GetArrayLength() - 1];

        var latersItterationId = latestIteration.GetProperty("id").GetInt32();

        var changes = await _ado.GetIterationChanges("", prId, latersItterationId);

        //var comments = await _ado.GetPRCommentsAsync(project, prId);

        var title = pr.TryGetProperty("title", out var t) ? t.GetString() : "N/A";
        var description = pr.TryGetProperty("description", out var d) ? d.GetString() : "⚠️ No description provided";
        var author = pr.TryGetProperty("createdBy", out var cb) &&
                     cb.TryGetProperty("displayName", out var dn)
                     ? dn.GetString() : "Unknown";
        var targetBranch = pr.TryGetProperty("targetRefName", out var tb) ? tb.GetString() : "N/A";

        // 2. Build user message with all PR context
        var userMessage = $"""
            ## Pull Request #{prId}
            **Title:** {title}
            **Description:** {description}
            **Author:** {author}
            **Target branch:** {targetBranch}

            ## Changed files / diff
            {diff}

           
            """;

    //model: "claude-opus-4-1",
    //        maxTokens: 1024,
    //        system: _skillContent,

        // 3. Call Claude with skill as system prompt
        _logger.LogInformation("Sending PR to Claude for analysis...");
        //_claude = 


        Message response = null;
        try
        {

            response = await _claude.Messages.Create(
           
                new Anthropic.Models.Messages.MessageCreateParams
                {
                    Model = "claude-opus-4-1",
                    MaxTokens = 1024,
                    System = _skillContent,
                     Messages = [new() { Role = Anthropic.Models.Messages.Role.User, Content = userMessage }]
                }
            );
        }
        catch ( Exception ex)
        {
            var x = ex;
        }

        // 4. Parse JSON verdict from Claude's response
        var raw = response.Content
            .OfType<Anthropic.Models.Messages.TextBlock>()
            .Select(b => b.Text)
            .FirstOrDefault() ?? "{}";

        // Strip markdown fences if Claude wraps in ```json
        raw = raw.Replace("```json", "").Replace("```", "").Trim();

        PRVerdict verdict;
        try
        {
            verdict = JsonSerializer.Deserialize<PRVerdict>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Null verdict returned");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Claude response: {Raw}", raw);
            throw new InvalidOperationException($"Could not parse Claude verdict: {ex.Message}");
        }

        _logger.LogInformation("Verdict: {Verdict} (score: {Score})", verdict.Verdict, verdict.Score);

        // 5. Post comment back to the PR
        var emoji = verdict.Verdict == "PASSED" ? "✅" : "❌";
        var issuesList = verdict.Issues.Any()
            ? string.Join("\n", verdict.Issues.Select(i => $"- {i}"))
            : "_No issues found_";

        var comment = $"""
            ## {emoji} Claude PR Review — **{verdict.Verdict}** ({verdict.Score}/100)

            {verdict.Summary}

            **Issues found:**
            {issuesList}

            ---
            _Reviewed automatically by Claude AI_
            """;

        await _ado.PostCommentAsync(project, prId, comment);

        // 6. Set ADO status check (blocks merge if FAILED)
        if (pr.TryGetProperty("repository", out var repo) &&
            repo.TryGetProperty("id", out var repoId))
        {
            await _ado.SetStatusCheckAsync(project, repoId.GetString()!, prId, verdict.Verdict == "PASSED");
        }

        return verdict;
    }
}
