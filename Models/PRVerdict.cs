namespace PRReviewAgent.Models;

public record PRVerdict(
    string Verdict,
    int Score,
    List<string> Issues,
    string Summary
);

public record WebhookPayload(
    string Project,
    int PullRequestId
);
