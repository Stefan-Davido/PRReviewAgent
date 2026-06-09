# PR Review Agent

AI-powered Azure DevOps Pull Request reviewer using Claude (Anthropic).

Automatically analyses PRs and posts a **PASSED / FAILED** verdict as a comment,
plus sets a status check that can block merging.

---

## Setup

### 1. Configure credentials

Edit `appsettings.json`:

```json
{
  "Anthropic": {
    "ApiKey": "YOUR_ANTHROPIC_API_KEY"
  },
  "AzureDevOps": {
    "Token": "YOUR_ADO_PAT_TOKEN",
    "OrgUrl": "https://dev.azure.com/YOUR_ORG"
  }
}
```

- Get your Anthropic API key at: https://console.anthropic.com
- Create an ADO Personal Access Token with scopes: `Code (Read)`, `Pull Request Threads (Read & Write)`

### 2. Run locally

```bash
dotnet run
```

Swagger UI opens at: http://localhost:5000

### 3. Manual trigger

```bash
curl -X POST http://localhost:5000/api/review/review \
  -H "Content-Type: application/json" \
  -d '{ "project": "MyProject", "pullRequestId": 42 }'
```

---

## Endpoints

| Method | URL | Description |
|--------|-----|-------------|
| POST | `/api/review/review` | Manually trigger a review |
| POST | `/api/review/webhook` | Azure DevOps service hook receiver |
| GET  | `/api/review/health` | Health check |

---

## Azure DevOps Webhook Setup

1. Go to **Project Settings → Service Hooks → + New**
2. Select **Web Hooks**
3. Trigger on: `Pull request created` and `Pull request updated`
4. URL: `https://your-deployed-host/api/review/webhook`

---

## Customising review rules

Edit `skills/pr-reviewer/SKILL.md` to change:
- What counts as a critical/major/minor issue
- Scoring weights
- Verdict thresholds

No code changes needed — just edit the markdown file and restart the app.

---

## Deploy to Azure App Service

```bash
dotnet publish -c Release
az webapp create --name pr-review-agent --runtime "DOTNETCORE:8.0"
az webapp deploy --src-path ./bin/Release/net8.0/publish
```

## Deploy with Docker

```bash
docker build -t pr-review-agent .
docker run -p 8080:8080 \
  -e Anthropic__ApiKey=your-key \
  -e AzureDevOps__Token=your-token \
  -e AzureDevOps__OrgUrl=https://dev.azure.com/your-org \
  pr-review-agent
```
