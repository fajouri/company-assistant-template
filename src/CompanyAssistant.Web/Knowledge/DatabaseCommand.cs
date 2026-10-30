using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CompanyAssistant.Web.Knowledge;

internal static class DatabaseCommand
{
    public static async Task<int> RunAsync(string command, string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        using var services = new ServiceCollection()
            .AddLogging(builder => builder.AddSimpleConsole(options => options.SingleLine = true))
            .BuildServiceProvider();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("CompanyAssistant");

        return command switch
        {
            "migrate" => await MigrationRunner.RunAsync(configuration, logger),
            "ingest" => await IngestionRunner.RunAsync(configuration, logger),
            _ => throw new InvalidOperationException($"Unknown database command '{command}'.")
        };
    }
}

internal static class IngestionRunner
{
    public static async Task<int> RunAsync(IConfiguration configuration, ILogger logger)
    {
        var options = IngestionOptions.Load(configuration);
        var embeddingSettings = EmbeddingSettings.Load(configuration);
        var databaseSettings = DatabaseSettings.Load(configuration);

        if (!embeddingSettings.IsConfigured)
        {
            logger.LogError(
                "Embeddings are not configured. Set Embeddings:Endpoint, Embeddings:ApiKey and Embeddings:Deployment.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        IEmbeddingClient embeddings = new FoundryEmbeddingClient(embeddingSettings, http);
        await using var store = new NpgsqlIngestionStore(databaseSettings);
        var service = new IngestionService(store, embeddings, options);

        var report = await service.RunAsync(CancellationToken.None);
        logger.LogInformation(
            "Ingestion finished: {Indexed} indexed, {Unchanged} unchanged, {Deleted} deleted, " +
            "{SkippedUnsafe} unsafe, {Oversized} oversized, {Failed} failed.",
            report.Indexed, report.Unchanged, report.Deleted, report.SkippedUnsafe, report.Oversized, report.Failed);

        return report.HasFailures ? 1 : 0;
    }
}
