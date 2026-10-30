using CompanyAssistant.Web.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.AddEntraIdentity();

var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/health/live");
app.MapGet("/api/branding", (IConfiguration config) => new
{
    companyName = config["Branding:CompanyName"] ?? "NovaWorks",
    assistantName = config["Branding:AssistantName"] ?? "Nova Assistant",
    logoUrl = LocalLogoPath.OrDefault(config["Branding:LogoUrl"]),
    defaultLanguage = config["Branding:DefaultLanguage"] == "en" ? "en" : "es"
});
app.MapIdentityEndpoints();
app.Run();

public partial class Program;
