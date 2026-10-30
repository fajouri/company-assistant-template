using System.Text;
using System.Text.Json;
using CompanyAssistant.Web.Identity;
using CompanyAssistant.Web.Knowledge;
using Microsoft.AspNetCore.Antiforgery;

namespace CompanyAssistant.Web.Chat;

internal static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/chat", ChatAsync)
            .RequireAuthorization(IdentityPolicies.ForPermission(AppPermissions.AssistantUse));
    }

    private static async Task ChatAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        ChatService chat,
        ConversationStore conversations,
        ChatRateLimiter rateLimiter,
        ChatSettings chatSettings,
        DatabaseSettings database,
        IEmbeddingClient embeddings,
        CancellationToken ct)
    {
        // CSRF validation runs before any body is read or work is done.
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "A valid antiforgery token is required." });
            return;
        }

        ChatRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<ChatRequest>(ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "A JSON body with a 'message' string is required." });
            return;
        }

        var message = request?.Message?.Trim() ?? string.Empty;
        if (message.Length == 0 || message.Length > chatSettings.MaxQueryLength)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = $"Message must be between 1 and {chatSettings.MaxQueryLength} characters." });
            return;
        }

        var permissions = RolePermissions.For(context.User);
        var tenantId = context.User.FindFirst("tid")?.Value;
        var objectId = context.User.FindFirst("oid")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(objectId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!database.IsRuntimeConfigured || !embeddings.IsConfigured || !chatSettings.IsConfigured)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Chat is not configured." });
            return;
        }

        if (!rateLimiter.TryAcquire(objectId, DateTimeOffset.UtcNow))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "60";
            await context.Response.WriteAsJsonAsync(new { error = "Too many requests. Please wait a moment and try again." });
            return;
        }

        Conversation conversation;
        if (!string.IsNullOrWhiteSpace(request?.ConversationId))
        {
            var owned = conversations.FindOwned(request.ConversationId, tenantId, objectId);
            if (owned is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            conversation = owned;
        }
        else
        {
            conversation = conversations.Create(tenantId, objectId);
        }

        var history = conversation.Turns;
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/x-ndjson; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(chatSettings.TimeoutSeconds));
        var token = timeoutCts.Token;

        var answer = new StringBuilder();
        DoneEvent? completed = null;
        try
        {
            await foreach (var chatEvent in chat.AnswerAsync(message, new RetrievalCaller(permissions), history, token))
            {
                switch (chatEvent)
                {
                    case SourcesEvent sources:
                        await WriteEventAsync(context, new
                        {
                            type = "sources",
                            citations = SerializeCitations(sources.Citations)
                        }, token);
                        break;
                    case DeltaEvent delta:
                        answer.Append(delta.Text);
                        await WriteEventAsync(context, new { type = "delta", text = delta.Text, provisional = true }, token);
                        break;
                    case DoneEvent done:
                        completed = done;
                        await WriteEventAsync(context, new
                        {
                            type = "done",
                            conversationId = conversation.Id,
                            limited = done.Limited,
                            citations = SerializeCitations(done.Citations)
                        }, token);
                        break;
                }
            }

            if (completed is null)
                throw new ChatUnavailableException("Chat stream ended before completion.");
            if (!token.IsCancellationRequested && !completed.Limited && answer.Length > 0 && completed.Sources is { Count: > 0 })
            {
                conversations.AppendExchange(conversation, message, answer.ToString(), completed.Sources);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client aborted the request; there is no response left to write.
        }
        catch (OperationCanceledException)
        {
            await WriteErrorAsync(context, "timeout");
        }
        catch (Exception)
        {
            if (ct.IsCancellationRequested) return;
            await WriteErrorAsync(context, "provider");
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, string code)
    {
        try
        {
            await WriteEventAsync(context, new
            {
                type = "error",
                code,
                message = "The assistant service is unavailable. Please try again later."
            }, CancellationToken.None);
        }
        catch (IOException) { /* Disconnected client. */ }
    }

    private static IEnumerable<object> SerializeCitations(IReadOnlyList<ChatCitation> citations) =>
        citations.Select(citation => new
        {
            index = citation.Index,
            chunkId = citation.ChunkId,
            documentId = citation.DocumentId,
            title = citation.Title,
            version = citation.Version
        });

    private static async Task WriteEventAsync(HttpContext context, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        await context.Response.WriteAsync(json + "\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
}

