using CompanyAssistant.Web.Identity;

namespace CompanyAssistant.Web.Knowledge;

internal static class KnowledgeEndpoints
{
    private const int MaxResults = 10;
    private const int MaxQueryLength = 2000;

    public static void MapKnowledgeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/search", SearchAsync)
            .RequireAuthorization(IdentityPolicies.AssignedUser);

        endpoints.MapGet("/api/documents/{documentId}", GetDocumentAsync)
            .RequireAuthorization(IdentityPolicies.AssignedUser);
    }

    private static async Task<IResult> SearchAsync(
        HttpContext context,
        RetrievalService retrieval,
        IEmbeddingClient embeddings,
        DatabaseSettings database,
        CancellationToken ct)
    {
        SearchRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SearchRequest>(ct);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            return Results.BadRequest(new { error = "A JSON body with a 'query' string is required." });
        }

        var query = request?.Query?.Trim() ?? string.Empty;
        if (query.Length == 0 || query.Length > MaxQueryLength)
            return Results.BadRequest(new { error = $"Query must be between 1 and {MaxQueryLength} characters." });

        var permissions = RolePermissions.For(context.User);
        if (AccessPolicyMapping.AllowedFor(permissions).Count == 0)
            return Results.Forbid();

        if (!database.IsRuntimeConfigured || !embeddings.IsConfigured)
            return Results.Json(new { error = "Retrieval is not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var queryEmbedding = (await embeddings.EmbedAsync([query], ct))[0];
        var results = await retrieval.SearchAsync(queryEmbedding, new RetrievalCaller(permissions), MaxResults, ct);

        return Results.Ok(results.Select(chunk => new
        {
            chunkId = chunk.ChunkId,
            documentId = chunk.DocumentId,
            title = chunk.Title,
            version = chunk.Version,
            chunkIndex = chunk.ChunkIndex,
            sectionHeading = chunk.SectionHeading,
            content = chunk.Content,
            distance = chunk.Distance
        }));
    }

    private static async Task<IResult> GetDocumentAsync(
        string documentId,
        HttpContext context,
        RetrievalService retrieval,
        DatabaseSettings database,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(documentId) || documentId.Length > 128)
            return Results.BadRequest();

        var permissions = RolePermissions.For(context.User);
        if (AccessPolicyMapping.AllowedFor(permissions).Count == 0)
            return Results.Forbid();

        if (!database.IsRuntimeConfigured)
            return Results.Json(new { error = "Retrieval is not configured." }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var document = await retrieval.GetSourceDocumentAsync(documentId, new RetrievalCaller(permissions), ct);
        if (document is null)
            return Results.NotFound();

        return Results.Ok(new
        {
            documentId = document.DocumentId,
            title = document.Title,
            version = document.Version,
            accessPolicy = document.AccessPolicy,
            effectiveDate = document.EffectiveDate.ToString("yyyy-MM-dd"),
            approvalState = document.ApprovalState,
            sourcePath = document.SourcePath,
            content = document.Content
        });
    }

    private sealed record SearchRequest(string? Query);
}
