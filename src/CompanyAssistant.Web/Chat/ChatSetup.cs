using CompanyAssistant.Web.Knowledge;
using Microsoft.SemanticKernel.ChatCompletion;

namespace CompanyAssistant.Web.Chat;

internal static class ChatSetup
{
    public static void AddChat(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(provider =>
            ChatSettings.Load(provider.GetRequiredService<IConfiguration>()));
        builder.Services.AddSingleton<ConversationStore>();
        builder.Services.AddSingleton(provider =>
            new ChatRateLimiter(provider.GetRequiredService<ChatSettings>().RequestsPerMinute));
        builder.Services.AddSingleton<FoundryChatCompletionService>(provider =>
            new FoundryChatCompletionService(
                provider.GetRequiredService<ChatSettings>(),
                // Timeout is enforced per request via a linked cancellation token; the HTTP
                // client itself stays unbounded so long streams are not cut mid-response.
                new HttpClient { Timeout = Timeout.InfiniteTimeSpan }));
        builder.Services.AddSingleton<IChatCompletionService>(provider =>
            provider.GetRequiredService<FoundryChatCompletionService>());
        builder.Services.AddSingleton<ChatService>();
    }
}
