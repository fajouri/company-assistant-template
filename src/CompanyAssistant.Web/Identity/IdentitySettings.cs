namespace CompanyAssistant.Web.Identity;

internal sealed class IdentityStartupValidation(EntraIdentitySettings settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = settings;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed record EntraIdentitySettings(Guid TenantId, Guid ClientId, string Issuer)
{
    public static EntraIdentitySettings LoadAndValidate(IConfiguration configuration)
    {
        var section = configuration.GetSection("AzureAd");
        var instance = section["Instance"];
        if (!Uri.TryCreate(instance, UriKind.Absolute, out var instanceUri)
            || instanceUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(instanceUri.Host)
            || instanceUri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(instanceUri.Query)
            || !string.IsNullOrEmpty(instanceUri.Fragment)
            || !string.IsNullOrEmpty(instanceUri.UserInfo))
            throw new InvalidOperationException("AzureAd:Instance must be an absolute HTTPS origin URI without a path, user info, query or fragment.");

        if (!Guid.TryParse(section["TenantId"], out var tenantId) || tenantId == Guid.Empty)
            throw new InvalidOperationException("AzureAd:TenantId must be a non-empty tenant GUID.");
        if (!Guid.TryParse(section["ClientId"], out var clientId) || clientId == Guid.Empty)
            throw new InvalidOperationException("AzureAd:ClientId must be a non-empty application GUID.");
        if (string.IsNullOrWhiteSpace(section["ClientSecret"]))
            throw new InvalidOperationException("AzureAd:ClientSecret must be supplied through secret configuration.");
        if (section["CallbackPath"] != IdentityPaths.SignInCallbackPath)
            throw new InvalidOperationException($"AzureAd:CallbackPath must be {IdentityPaths.SignInCallbackPath}.");
        if (section["SignedOutCallbackPath"] != IdentityPaths.SignedOutCallbackPath)
            throw new InvalidOperationException($"AzureAd:SignedOutCallbackPath must be {IdentityPaths.SignedOutCallbackPath}.");
        if (section["RemoteSignOutPath"] != IdentityPaths.RemoteSignOutPath)
            throw new InvalidOperationException($"AzureAd:RemoteSignOutPath must be {IdentityPaths.RemoteSignOutPath}.");

        return new EntraIdentitySettings(
            tenantId,
            clientId,
            $"{instanceUri.AbsoluteUri.TrimEnd('/')}/{tenantId:D}/v2.0");
    }
}
