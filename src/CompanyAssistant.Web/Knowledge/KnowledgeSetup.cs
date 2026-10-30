namespace CompanyAssistant.Web.Knowledge;

internal static class KnowledgeSetup
{
    public static void AddKnowledge(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(provider =>
            DatabaseSettings.Load(provider.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton(provider =>
            EmbeddingSettings.Load(provider.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton<IRetrievalStore, NpgsqlRetrievalStore>();
        builder.Services.AddSingleton<RetrievalService>();
        builder.Services.AddSingleton<IEmbeddingClient>(provider =>
            new FoundryEmbeddingClient(
                provider.GetRequiredService<EmbeddingSettings>(),
                new HttpClient { Timeout = TimeSpan.FromSeconds(60) }));
    }
}

