using System.Text;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CompanyAssistant.Web.Tests;

public sealed class TokenValidationTests(TestApplicationFactory factory) : IClassFixture<TestApplicationFactory>
{
    private static readonly SymmetricSecurityKey TestKey = new(
        Encoding.UTF8.GetBytes("fictional-test-signing-key-32-bytes!"));

    [Fact]
    public async Task Valid_test_fixture_passes_configured_audience_and_issuer_validation()
    {
        var result = await Validate(TestApplicationFactory.TenantId, TestApplicationFactory.ClientId);
        Assert.True(result.IsValid, result.Exception?.ToString());
    }

    [Fact]
    public async Task Wrong_tenant_is_rejected_by_token_validation()
    {
        var result = await Validate(TestApplicationFactory.OtherTenantId, TestApplicationFactory.ClientId);
        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidIssuerException>(result.Exception);
    }

    [Fact]
    public async Task Wrong_audience_is_rejected_by_token_validation()
    {
        var result = await Validate(TestApplicationFactory.TenantId, TestApplicationFactory.OtherTenantId);
        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception);
    }

    private async Task<TokenValidationResult> Validate(string tenantId, string audience)
    {
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
            Audience = audience,
            Claims = new Dictionary<string, object>
            {
                ["tid"] = tenantId,
                ["oid"] = TestApplicationFactory.ObjectId,
                ["roles"] = new[] { "Employee" },
                ["name"] = "Fictional Test User"
            },
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(TestKey, SecurityAlgorithms.HmacSha256)
        });

        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var parameters = options.TokenValidationParameters.Clone();
        // Replace only remote metadata and signing keys with deterministic test fixtures.
        parameters.ConfigurationManager = null;
        parameters.IssuerSigningKey = TestKey;
        parameters.IssuerSigningKeys = null;
        parameters.IssuerSigningKeyResolver = null;
        parameters.IssuerValidator = null;
        return await handler.ValidateTokenAsync(token, parameters);
    }
}
