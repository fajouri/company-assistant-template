# Chat

The assistant answers questions with grounded citations. It uses Semantic Kernel's chat
abstraction (`Microsoft.SemanticKernel.Core`) over a configurable Foundry/Azure-OpenAI-
compatible chat deployment. The normal path is deterministic: authorize the caller, retrieve
their allowed passages, stream one bounded provisional model generation, and confirm its reference
numbers only after normal provider completion. There are no
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

The response is `application/x-ndjson`, one JSON object per line:

1. `{"type":"delta","text":"...","provisional":true}` (zero or more incremental fragments)
2. `{"type":"sources","citations":[{"index":1,"chunkId":...,"documentId":"leave-policy","title":"...","version":"1.0"}]}`
3. `{"type":"done","conversationId":"...","limited":false,"citations":[...]}`

`sources` and `done.citations` contain only passages referenced by valid `[n]` markers in the
completed model text, never all retrieval candidates. The backend retains a bounded copy while
flushing each delta immediately. The client displays deltas as plain text with a persistent,
accessible “provisional / not yet verified; may contain errors” status. It confirms the answer
and shows final citations only after a successful `done` and clean NDJSON EOF. The completed
status says reference numbers were checked; it does not claim semantic verification.

Reference validation runs on the concatenated answer, including `[n]` markers split across
deltas. Missing or unknown indices produce `sources: []` and `done` with `limited: true`;
the client removes the provisional text and renders the localized limitation / HR referral.
Such exchanges are never saved as successful history. Citation-number validation cannot
prove that each factual claim is semantically supported by its cited passage. Provisional
streaming exposes text before these checks: incorrect or unreferenced content can briefly be
visible. Withdrawal cannot undo what a user has already read.

On provider/internal failure the stream ends with `{"type":"error","code":"provider","message":"..."}` containing
only a generic, non-leaking message. When retrieval returns no permitted passage the stream is
`{"type":"sources","citations":[]}` followed by `{"type":"done","limited":true,...}` and no
model call; the UI renders a localized HR-referral notice. The response is `no-store`, and the
client may abort the request at any time. The client requires exactly one terminal `done` or
`error` event; EOF without one, malformed events or data after a terminal event are errors.
Provider failure, transport failure and incomplete streams remove provisional text and citations
and show a localized failure notice. Server timeouts use error code `timeout` and a distinct
timeout notice. The Stop button aborts fetch and removes provisional text, showing a cancellation
notice; user aborts stop the server request without a terminal event. These paths never save a
successful exchange. Retrying continues from the last successful server-side history.

The provider transport uses choice `index: 0` (the request defaults to `n=1`). Only explicit
`finish_reason: "stop"` followed by `[DONE]` completes normally. `length`, `content_filter`,
tool/function calls, unknown reasons, missing reason/index, repeated selected completion frames,
selected deltas after completion and EOF before `[DONE]` fail closed with a generic provider
error. Empty-choice annotation/usage frames do not confirm completion; other choices cannot
confirm the selected choice. The request does not enable tools.

## Configuration

| Key | Default | Purpose |
| --- | --- | --- |
| `Chat__Endpoint` | – | Foundry/Azure-OpenAI-compatible base URL, e.g. `https://your-resource.openai.azure.com`. |
| `Chat__ApiKey` | – | Provider key (secret configuration only, never `appsettings.json`). |
| `Chat__Deployment` | – | Chat model deployment name, e.g. `gpt-4o-mini`. |
| `Chat__ApiVersion` | `2024-02-01` | Provider API version. |
| `Chat__MaxContextChunks` | `4` | Retrieved chunks included in the prompt. |
| `Chat__MaxContextChars` | `8000` | Accumulated character budget for the retrieved-passage section placed in the model prompt between `Reference passages:` and `Question:`. It counts, per included chunk, the citation number, brackets, title, document id, version, fixed separators and the chunk content. Chunks are kept in relevance order until the budget is exhausted; chunks that do not fit are omitted, and their content is truncated to the remaining budget. The system prompt, prior conversation turns, the `Reference passages:`/`Question:` wrappers and the user question are not counted. |
| `Chat__MaxHistoryTurns` | `4` | Prior question/answer turns included from the conversation. |
| `Chat__MaxQueryLength` | `2000` | Maximum question length. |
| `Chat__MaxOutputTokens` | `512` | `max_tokens` sent to the model and the streamed character bound. |
| `Chat__TimeoutSeconds` | `30` | Per-request timeout (retrieval + generation). |
| `Chat__RequestsPerMinute` | `10` | Sliding-window rate limit per authenticated user. |
| `Chat__MaxEvidenceDistance` | `0.35` | Maximum pgvector cosine distance (`<=>`) for a chat passage, in the range 0–2. |

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
`documents.version`. The server returns only referenced citations from the existing ACL-filtered
store. It rechecks every passage that could have influenced a prior answer by chunk id, document
id, version and content hash under the caller's current permissions before reusing that exchange.
Each successful answer carries the union of **all** current retrieval candidates and the complete
dependency sets of **all** prior exchanges included in its prompt or follow-up retrieval query,
not only the citations it displays. This propagates transitively across repeated reuse and keeps
ancestor stamps even after the ancestor exchange is evicted. Revoking HR, for example, omits
both the original HR exchange and any employee-policy answer influenced by it on the next request.
Unverifiable exchanges are omitted from the model prompt. This covers database changes to ACL,
approval, effective date, deletion, version and chunk content. It does not detect changes to a
source file until ingestion updates the database. Short follow-ups (at most 80 characters,
beginning with a small set of follow-up phrases such as “¿y…?” or “and…”) add the last verified
user question to the retrieval query, without adding a model call or a prior answer. Other
phrasing and ambiguous follow-ups may still retrieve the wrong topic.

Dependencies are deduplicated server-side and capped at
`MaxContextChunks * (MaxHistoryTurns + 1)` stamps per answer (20 with defaults). History selection
reserves `MaxContextChunks` stamps for current retrieval, considers at most `MaxHistoryTurns`
exchanges newest-first, and omits a whole exchange if its full dependencies do not fit. It never
truncates dependency sets. This can reduce follow-up context in long conversations. Exchanges
without source stamps are excluded, including their user questions; the client cannot submit
history, source stamps or permissions. State is in-memory only, so deployment restarts discard
old exchanges rather than treating pre-change direct-only provenance as transitive.

Chat filters retrieved passages by cosine distance after the authorization-aware SQL search;
the retrieval API itself is unchanged. `0.35` is a starting bound, not a measure of answer
accuracy. Calibrate it with representative approved questions, unrelated questions, the chosen
embedding deployment and the evaluation set; inspect false positives and false negatives before
using it with real data. Changing embedding models requires recalibration.

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
   Verify provisional text appears before completion, that only a normally completed answer
   gets final `leave-policy` citations, that the Employee prompt has no HR passages, and that
   "What is the CEO's salary?" returns the localized HR-referral limitation.
5. From the browser dev tools, confirm the response is `application/x-ndjson`, cached as
   `no-store`, that deltas carry `provisional: true`, and that an in-flight request can be
   aborted with the Stop button (text removed, cancellation notice). Check timeout and failure
   withdrawal separately using a controlled test deployment. Review proxy buffering settings
   if deltas arrive together despite incremental flushes.
6. Record latency and usage against the evaluation question set; do not commit answers or data.

Real-integration verification is reported separately from the deterministic test suite and is
never implied by a passing build.
