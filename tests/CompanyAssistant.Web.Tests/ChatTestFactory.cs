using System.Security.Claims;
using System.Text.Encodings.Web;
using CompanyAssistant.Web.Chat;
using CompanyAssistant.Web.Knowledge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CompanyAssistant.Web.Tests;

internal sealed class ChatTestFactory(int requestsPerMinute = 10, bool configureChat = true) : WebApplicationFactory<Program>
{
    internal FakeRetrievalStore RetrievalStore { get; } = new();
    internal FakeEmbeddingClient Embeddings { get; } = new();
    internal FakeChatCompletionService ChatCompletion { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(TestApplicationFactory.TestConfiguration());
            var values = new Dictionary<string, string?>
            {
                ["Database:RuntimeConnectionString"] = "Host=test;Database=test;Username=test;Password=test",
                ["Database:IngestionConnectionString"] = "Host=test;Database=test;Username=test;Password=test",
                ["Embeddings:Endpoint"] = "https://embeddings.invalid",
                ["Embeddings:ApiKey"] = "fictional-test-key",
                ["Embeddings:Deployment"] = "text-embedding-3-small"
            };
            if (configureChat)
            {
                values["Chat:Endpoint"] = "https://chat.invalid";
                values["Chat:ApiKey"] = "fictional-test-key";
                values["Chat:Deployment"] = "gpt-test";
                values["Chat:RequestsPerMinute"] = requestsPerMinute.ToString();
            }

            configuration.AddInMemoryCollection(values);
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestApplicationFactory.Scheme;
                    options.DefaultAuthenticateScheme = TestApplicationFactory.Scheme;
                    options.DefaultChallengeScheme = TestApplicationFactory.Scheme;
                    options.DefaultForbidScheme = TestApplicationFactory.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestIdentityHandler>(TestApplicationFactory.Scheme, _ => { });

            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    AuthorizationEndpoint = "https://login.microsoftonline.com/test/authorize",
                    TokenEndpoint = "https://login.microsoftonline.com/test/token",
                    EndSessionEndpoint = "https://login.microsoftonline.com/test/logout"
                };
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });

            services.AddSingleton<IRetrievalStore>(RetrievalStore);
            services.AddSingleton<IEmbeddingClient>(Embeddings);
            services.AddSingleton<IChatCompletionService>(ChatCompletion);
        });
    }
}
