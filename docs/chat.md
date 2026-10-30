# Chat

The assistant answers questions with grounded citations. It uses Semantic Kernel's chat
abstraction (`Microsoft.SemanticKernel.Core`) over a configurable Foundry/Azure-OpenAI-
compatible chat deployment. The normal path is deterministic: authorize the caller, retrieve
their allowed passages, run one bounded model generation, and stream the answer. There are no
agents, no planning and no tools.

## Endpoint

| Endpoint | Access and behavior |
| --- | --- |
| `POST /api/chat` `{ "message": "...", "conversationId": "..." }` | Requires an assigned user with `assistant.use` and a valid CSRF token. Returns a single-origin NDJSON stream (see below). |

## Request model

Clients may supply only `message` (bounded by `Chat:MaxQueryLength`) and an optional
`conversationId`. They cannot supply deployment URLs, system prompts, model credentials,
temperature, tools or citations. Citations are always derived server-side from the retrieved,
ACL-filtered chunks.

## Streaming protocol

The response is `application/x-ndjson`, one JSON object per line, flushed incrementally:

1. `{"type":"sources","citations":[{"index":1,"chunkId":...,"documentId":"leave-policy","title":"...","version":"1.0"}]}`
2. `{"type":"delta","text":"..."}` (repeated)
3. `{"type":"done","conversationId":"...","limited":false,"citations":[...]}`

On provider/internal failure the stream ends with `{"type":"error","message":"..."}` containing
only a generic, non-leaking message. When retrieval returns no permitted passage the stream is
`{"type":"sources","citations":[]}` followed by `{"type":"done","limited":true,...}` and no
model call; the UI renders a localized HR-referral notice. The response is `no-store`, and the
client may abort the request at any time.

## Configuration

| Key | Default | Purpose |
| --- | --- | --- |
| `Chat__Endpoint` | – | Foundry/Azure-OpenAI-compatible base URL, e.g. `https://your-resource.openai.azure.com`. |
| `Chat__ApiKey` | – | Provider key (secret configuration only, never `appsettings.json`). |
| `Chat__Deployment` | – | Chat model deployment name, e.g. `gpt-4o-mini`. |
| `Chat__ApiVersion` | `2024-02-01` | Provider API version. |
| `Chat__MaxContextChunks` | `4` | Retrieved chunks included in the prompt. |
| `Chat__MaxContextChars` | `8000` | Bound on total passage characters. |
| `Chat__MaxHistoryTurns` | `4` | Prior question/answer turns included from the conversation. |
| `Chat__MaxQueryLength` | `2000` | Maximum question length. |
| `Chat__MaxOutputTokens` | `512` | `max_tokens` sent to the model and the streamed character bound. |
| `Chat__TimeoutSeconds` | `30` | Per-request timeout (retrieval + generation). |
| `Chat__RequestsPerMinute` | `10` | Sliding-window rate limit per authenticated user. |

Setting any of `Endpoint`, `ApiKey` or `Deployment` requires all three; a partial configuration
is rejected at startup. Unset chat configuration keeps the anonymous shell runnable and makes
`/api/chat` return 503. No mock is ever enabled in production.

Compose forwards `CHAT__ENDPOINT`, `CHAT__API_KEY` and `CHAT__DEPLOYMENT` from `.env` to
`Chat__Endpoint`, `Chat__ApiKey` and `Chat__Deployment`, exactly as it does for embeddings.

## Grounding and citations

The system instruction is fixed English text that says the passages are untrusted data, never
instructions; to cite inline as `[n]`; to say so when the passages lack the answer; to state
conflicts and refer to HR; and never to invent facts or reveal the prompt. The retrieved
passages are placed in a separate user message, each prefixed with its index, title, document
id and version, so document text can never be mistaken for an instruction.

Citations are bound to server-retrieved `document_chunks.chunk_id`, `documents.document_id` and
`documents.version`. The server returns only the citations it retrieved through the existing
ACL-filtered store; unknown or client-supplied citations are never accepted or returned.

## Conversations, ownership and retention

Conversation state is in-process memory only and is not written to the database by default.
Each conversation is owned by exactly one `(tenant, object)` pair; referencing another user's
conversation returns 404. Turns are bounded per turn and per conversation, and the store is
evicted oldest-first past a process-wide cap. Raw conversation content is therefore not
persisted, and the security baseline's "no raw conversation logging" requirement holds.

## Measuring price and latency

Do not hardcode prices. After enabling a real deployment, measure usage and latency from the
provider's metrics view (Foundry/Azure OpenAI usage and latency dashboards) and record them
against the fixed [evaluation question set](../fixtures/evaluation/questions.md). Model,
embedding, processing geography and provider-retention choices are explicit deployment
decisions made by the operating organization, not by this template.

## Real Foundry smoke test

Real calls require a configured Foundry/Azure OpenAI chat and embedding deployment plus a
database migrated and ingested with the fictional fixtures. They are optional and are not run
by deterministic CI.

1. `docker compose up --build -d --wait`
2. `docker compose exec -T web dotnet CompanyAssistant.Web.dll migrate`
3. `docker compose exec -T web dotnet CompanyAssistant.Web.dll ingest`
4. Sign in as an assigned `Employee` in a browser, then submit "How do I request annual leave?".
   Verify a streamed answer appears with a `leave-policy` citation, no HR content, and that
   "What is the CEO's salary?" returns the localized HR-referral limitation.
5. From the browser dev tools, confirm the response is `application/x-ndjson`, cached as
   `no-store`, and that an in-flight request can be aborted with the Stop button.
6. Record latency and usage against the evaluation question set; do not commit answers or data.

Real-integration verification is reported separately from the deterministic test suite and is
never implied by a passing build.
