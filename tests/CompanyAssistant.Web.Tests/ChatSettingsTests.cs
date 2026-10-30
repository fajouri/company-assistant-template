using CompanyAssistant.Web.Chat;
using Microsoft.Extensions.Configuration;

namespace CompanyAssistant.Web.Tests;

public sealed class ChatSettingsTests
{
    private static ChatSettings Load(params (string Key, string Value)[] values)
    {
        var data = new Dictionary<string, string?>();
        foreach (var (key, value) in values)
            data[key] = value;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
        return ChatSettings.Load(configuration);
    }

    [Fact]
    public void Empty_configuration_is_not_configured()
    {
        var settings = Load();
        Assert.False(settings.IsConfigured);
        Assert.Equal(ChatSettings.DefaultMaxOutputTokens, settings.MaxOutputTokens);
    }

    [Theory]
    [InlineData("Chat:Endpoint", "https://chat.invalid")]
    [InlineData("Chat:ApiKey", "key")]
    [InlineData("Chat:Deployment", "gpt-test")]
    public void Partial_configuration_fails_closed(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => Load((key, value)));
    }

    [Theory]
    [InlineData("Chat:Endpoint", "not-a-url")]
    [InlineData("Chat:Endpoint", "ftp://chat.invalid")]
    public void Invalid_endpoint_fails_closed(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => Load(
            ("Chat:Endpoint", "https://chat.invalid"),
            ("Chat:ApiKey", "key"),
            ("Chat:Deployment", "gpt-test"),
            (key, value)));
    }

    [Theory]
    [InlineData("Chat:MaxOutputTokens", "0")]
    [InlineData("Chat:MaxOutputTokens", "999999")]
    [InlineData("Chat:TimeoutSeconds", "1")]
    [InlineData("Chat:RequestsPerMinute", "0")]
    public void Out_of_range_bounds_fail_closed(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => Load(
            ("Chat:Endpoint", "https://chat.invalid"),
            ("Chat:ApiKey", "key"),
            ("Chat:Deployment", "gpt-test"),
            (key, value)));
    }

    [Fact]
    public void Full_configuration_is_configured()
    {
        var settings = Load(
            ("Chat:Endpoint", "https://chat.invalid"),
            ("Chat:ApiKey", "key"),
            ("Chat:Deployment", "gpt-test"));
        Assert.True(settings.IsConfigured);
        Assert.Equal("https://chat.invalid", settings.Endpoint);
    }

    [Fact]
    public void Evidence_distance_is_configurable_and_bounded()
    {
        var configured = Load(("Chat:MaxEvidenceDistance", "0.42"));
        Assert.Equal(0.42, configured.MaxEvidenceDistance);
        Assert.Throws<InvalidOperationException>(() => Load(("Chat:MaxEvidenceDistance", "NaN")));
        Assert.Throws<InvalidOperationException>(() => Load(("Chat:MaxEvidenceDistance", "2.1")));
    }
}
