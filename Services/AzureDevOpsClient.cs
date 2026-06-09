using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PRReviewAgent.Services;

public class AzureDevOpsClient
{
    private readonly HttpClient _http;
    private readonly string _orgUrl;
    private readonly ILogger<AzureDevOpsClient> _logger;

    public AzureDevOpsClient(string token, string orgUrl, ILogger<AzureDevOpsClient> logger)
    {
        _orgUrl = orgUrl.TrimEnd('/');
        _logger = logger;
        _http = new HttpClient();
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonElement> GetPullRequestAsync(string project, int prId)
    {
        var url = $"{_orgUrl}/{project}/_apis/git/pullrequests/{prId}?api-version=7.1";
        _logger.LogInformation("Fetching PR {PrId} from {Url}", prId, url);
        var response = await _http.GetStringAsync(url);
        return JsonDocument.Parse(response).RootElement;
    }

    public async Task<string> GetPRDiffAsync(string project, int prId)
    {
        string response = string.Empty;
        // Fetch changed file iterations
        var url = $"{_orgUrl}/{project}/_apis/git/pullrequests/{prId}/iterations?api-version=7.1";
        try
        {

         response = await _http.GetStringAsync(url);
        }
        catch(Exception ex)
        {
            var url2 = $"{_orgUrl}/{project}/_apis/git/repositories/{repositoryId}/pullRequests/{prId}/iterations?api-version=7.1";
            response = await _http.GetStringAsync(url2);
        }

        // Truncate if too large to fit in Claude context window
        const int maxLength = 30000;
        if (response.Length > maxLength)
        {
            _logger.LogWarning("Diff truncated from {Original} to {Max} chars", response.Length, maxLength);
            return response[..maxLength] + "\n\n... (diff truncated due to size)";
        }

        return response;
    }

    public async Task<JsonElement> GetIterationChanges(string repositoryId, int pullRequestId, int iterationId)
    {
        //var url = $"{_orgUrl}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/iterations/{iterationId}/changes?api-version=7.1";

        var url = $"{_orgUrl}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/iterations/{iterationId}?api-version=7.1";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task ProcessPullRequestChanges(string repositoryId, int pullRequestId)
    {
        var iterationId = await GetLatestIterationId(repositoryId, pullRequestId);
        var changes = await GetIterationChanges(repositoryId, pullRequestId, iterationId);

        // changes.GetProperty("changeEntries") contains the file changes
    }

    private async Task<int> GetLatestIterationId(string repositoryId, int pullRequestId)
    {
        var url = $"{_orgUrl}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}/iterations?api-version=7.1";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json);

        var iterations = result.GetProperty("value");
        var latestIteration = iterations[iterations.GetArrayLength() - 1];

        return latestIteration.GetProperty("id").GetInt32();
    }

    public async Task<string> GetPRCommentsAsync(string project, int prId)
    {
        var url = $"{_orgUrl}/{project}/_apis/git/pullrequests/{prId}/threads?api-version=7.1";
        var response = await _http.GetStringAsync(url);
        return response.Length > 5000 ? response[..5000] + "\n... (truncated)" : response;
    }

    public async Task PostCommentAsync(string project, int prId, string comment)
    {
        var url = $"{_orgUrl}/{project}/_apis/git/pullrequests/{prId}/threads?api-version=7.1";
        var body = JsonSerializer.Serialize(new
        {
            comments = new[]
            {
                new { content = comment, commentType = 1 }
            },
            status = 1
        });

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var result = await _http.PostAsync(url, content);

        if (!result.IsSuccessStatusCode)
        {
            var error = await result.Content.ReadAsStringAsync();
            _logger.LogError("Failed to post comment: {Error}", error);
        }
        else
        {
            _logger.LogInformation("Comment posted to PR {PrId}", prId);
        }
    }

    public async Task SetStatusCheckAsync(string project, string repoId, int prId, bool passed)
    {
        var url = $"{_orgUrl}/{project}/_apis/git/repositories/{repoId}/pullRequests/{prId}/statuses?api-version=7.1";
        var body = JsonSerializer.Serialize(new
        {
            state = passed ? "succeeded" : "failed",
            description = passed ? "Claude PR review passed" : "Claude PR review failed — see comments",
            context = new
            {
                name = "claude-pr-review",
                genre = "ai-review"
            }
        });

        var content = new StringContent(body, Encoding.UTF8, "application/json");
        var result = await _http.PostAsync(url, content);

        if (!result.IsSuccessStatusCode)
        {
            var error = await result.Content.ReadAsStringAsync();
            _logger.LogError("Failed to set status check: {Error}", error);
        }
    }
}
