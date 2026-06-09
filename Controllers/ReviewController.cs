using Microsoft.AspNetCore.Mvc;
using PRReviewAgent.Models;
using PRReviewAgent.Services;
using System.Text.Json;

namespace PRReviewAgent.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReviewController : ControllerBase
{
    private readonly PRReviewService _reviewer;
    private readonly ILogger<ReviewController> _logger;

    public ReviewController(PRReviewService reviewer, ILogger<ReviewController> logger)
    {
        _reviewer = reviewer;
        _logger = logger;
    }

    /// <summary>
    /// Manually trigger a PR review.
    /// POST /api/review/review
    /// Body: { "project": "MyProject", "pullRequestId": 42 }
    /// </summary>
    [HttpPost("review")]
    public async Task<IActionResult> Review([FromBody] WebhookPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Project))
            return BadRequest(new { error = "Project is required." });

        if (payload.PullRequestId <= 0)
            return BadRequest(new { error = "A valid PullRequestId is required." });

        try
        {
            var verdict = await _reviewer.ReviewAsync(payload.Project, payload.PullRequestId);
            return Ok(verdict);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Review failed for PR #{PrId}", payload.PullRequestId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Azure DevOps service hook webhook endpoint.
    /// Configure in ADO: Project Settings → Service Hooks → Web Hooks
    /// Trigger on: git.pullrequest.created and git.pullrequest.updated
    /// URL: https://your-host/api/review/webhook
    /// </summary>
    [HttpPost("webhook")]
    public IActionResult Webhook([FromBody] JsonElement body)
    {
        // ADO expects a fast response — fire and forget the review
        try
        {
            var eventType = body.GetProperty("eventType").GetString();

            if (eventType != "git.pullrequest.created" &&
                eventType != "git.pullrequest.updated")
            {
                _logger.LogInformation("Ignored ADO event: {EventType}", eventType);
                return Ok(new { message = "Event ignored." });
            }

            var resource = body.GetProperty("resource");
            var prId = resource.GetProperty("pullRequestId").GetInt32();
            var project = resource
                .GetProperty("repository")
                .GetProperty("project")
                .GetProperty("name")
                .GetString()!;

            _logger.LogInformation("Webhook received: {EventType} for PR #{PrId} in {Project}",
                eventType, prId, project);

            // Fire and forget — don't await, return 200 immediately to ADO
            _ = Task.Run(async () =>
            {
                try
                {
                    await _reviewer.ReviewAsync(project, prId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background review failed for PR #{PrId}", prId);
                }
            });

            return Ok(new { message = $"Review started for PR #{prId}." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook processing failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Health check endpoint.
    /// GET /api/review/health
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });
}
