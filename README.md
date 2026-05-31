# Claude Enterprise — ASP.NET Core 8 Web API + Web Console

Enterprise-grade reference implementation that wraps the official **Anthropic C# SDK** (`Anthropic` NuGet, v10 beta) behind a clean ASP.NET Core 8 Web API and serves a polished, dependency-free chat console.

## Architecture

Clean Architecture / Onion layering:

```
src/
├── ClaudeEnterprise.Domain          ← entities, value objects, repository contracts
├── ClaudeEnterprise.Application     ← DTOs, validators, service abstractions, options
├── ClaudeEnterprise.Infrastructure  ← Anthropic SDK adapter, in-memory persistence, DI
└── ClaudeEnterprise.Api             ← controllers, middleware, SSE streaming, wwwroot UI
```

### Cross-cutting concerns

| Concern        | Implementation                                                              |
| -------------- | --------------------------------------------------------------------------- |
| Logging        | Serilog (console sink, request logging, correlation via `TraceIdentifier`)  |
| Validation     | FluentValidation auto-validation on controller inputs                       |
| Errors         | Typed exception middleware → RFC 7807 `application/problem+json`            |
| Rate limiting  | `Microsoft.AspNetCore.RateLimiting` token-bucket, per remote IP             |
| CORS           | Configurable allow-list (`Cors:AllowedOrigins`)                             |
| Health         | `/health` with JSON UI response writer                                      |
| Docs           | Swagger / OpenAPI at `/swagger` (Development only)                          |
| Streaming      | Server-Sent Events backed by SDK `CreateStreamingAsync` `IAsyncEnumerable`  |
| Options        | `AnthropicOptions` bound + validated on start (`ValidateOnStart()`)         |
| Retries / TO   | Forwarded to SDK (`MaxRetries`, `Timeout` in `ClientOptions`)               |
| Forwarded hdrs | `UseForwardedHeaders` for proxy/ingress deployments                         |

## API

| Method | Route                              | Purpose                          |
| ------ | ---------------------------------- | -------------------------------- |
| POST   | `/api/chat/messages`               | Non-streaming completion         |
| POST   | `/api/chat/messages/stream`        | SSE streaming completion         |
| GET    | `/api/chat/conversations`          | List conversations               |
| GET    | `/api/chat/conversations/{id}`     | Get a conversation with history  |
| DELETE | `/api/chat/conversations/{id}`     | Delete a conversation            |
| GET    | `/health`                          | Liveness/readiness               |
| GET    | `/swagger`                         | OpenAPI UI (Development)         |

### Request example

```bash
curl -X POST https://localhost:7099/api/chat/messages \
  -H "content-type: application/json" \
  -d '{
    "message": "Summarize the SOLID principles in 5 bullets.",
    "model": "claude-sonnet-4-5",
    "maxTokens": 512,
    "temperature": 0.4
  }'
```

## Running

### 1. Provide your Anthropic API key

Pick **one**:

```powershell
# Environment variable (recommended)
$env:ANTHROPIC_API_KEY = "sk-ant-..."

# Or user secrets
dotnet user-secrets --project src/ClaudeEnterprise.Api set "Anthropic:ApiKey" "sk-ant-..."
```

### 2. Run

```powershell
dotnet restore
dotnet run --project src/ClaudeEnterprise.Api
```

Open <https://localhost:7099/> — the web console is served from `wwwroot`.

## Frontend (web console)

Located under [src/ClaudeEnterprise.Api/wwwroot](src/ClaudeEnterprise.Api/wwwroot). Zero build step, zero runtime dependencies — pure HTML/CSS/ES modules.

Features:

- Streaming responses with token-by-token rendering (SSE)
- Conversation sidebar with persistence and delete
- Per-request overrides for model, max tokens, temperature, system prompt
- Light / dark mode (follows OS)
- Health indicator polled every 30s
- Esc to cancel an in-flight stream
- Mobile-responsive layout

## Production checklist

- Replace `InMemoryConversationRepository` with EF Core / Cosmos / Redis.
- Add AuthN/AuthZ (`AddAuthentication().AddJwtBearer(...)`, `[Authorize]`).
- Centralize secrets in Key Vault / AWS Secrets Manager.
- Add OpenTelemetry exporters (traces + metrics).
- Tighten CORS, add CSP headers, enable HSTS preload.
- Containerize (`Dockerfile`) and deploy behind an ingress with TLS.
- For Bedrock / Vertex / Foundry deployments, swap `Anthropic` for
  `Anthropic.Bedrock` / `Anthropic.Vertex` / `Anthropic.Foundry` and adjust
  the DI registration in `Infrastructure/DependencyInjection.cs`.
# Claude-Enterprise-Console
