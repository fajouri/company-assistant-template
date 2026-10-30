# Entra ID identity setup

## Implemented flow

The backend uses Microsoft.Identity.Web 4.16.1 with single-tenant OpenID Connect, authorization code and PKCE. It requests only `openid` and `profile`; it does not request `offline_access`, call Graph or persist OIDC tokens. React never receives an ID, access or refresh token and does not use browser localStorage.

| Endpoint | Access and behavior |
| --- | --- |
| `GET /api/auth/login?returnUrl=/` | Starts Entra sign-in. Only canonical local return URLs are accepted; unsafe values fall back to `/`. |
| `GET /api/session` | Returns `isAuthenticated: false` anonymously. An authenticated response contains validated tenant/object identifiers, display name, recognized roles and permissions, never tokens. |
| `GET /api/auth/antiforgery` | Requires an assigned user and sets the protected antiforgery cookie plus a JavaScript-readable CSRF request-token cookie. |
| `POST /api/auth/logout` | Requires an assigned user and a matching CSRF cookie/header or form field, then signs out of the local cookie and Entra. |
| `GET /api/protected` | Requires `assistant.use`; returns `enabled: false` to prove authorization while chat remains disabled. |

The session cookie is `__Host-` prefixed, HTTP-only, Secure, SameSite=Lax and limited to eight hours with sliding expiration. OIDC correlation/nonce cookies are Secure and SameSite=None because the identity provider posts back cross-site. Session and antiforgery responses are not cached. Authentication libraries log at Warning or above; do not enable token/PII debug logging.

The three OIDC paths are intentionally distinct: `/signin-oidc` is the login callback, `/signout-callback-oidc` is the post-logout callback that Entra returns to (`post_logout_redirect_uri`) before the app redirects to `/`, and `/signout-oidc` is the front-channel (remote) logout URL. `OpenIdConnectHandler` matches the remote sign-out path before the signed-out callback path, so making them equal would swallow the post-logout return and skip the final redirect.

## Register the application

Use a test tenant for setup and verification. Do not use employee names, real identifiers or real conversations in source-controlled fixtures.

1. In **Microsoft Entra admin center > Entra ID > App registrations**, create a registration for **Accounts in this organizational directory only (single tenant)**. Record its application/client ID and directory/tenant ID.
2. Under **Authentication**, add **Web** redirect URIs. Use exact HTTPS URIs, not wildcards:
   - Local login: `https://localhost:5001/signin-oidc`
   - Local post-logout: `https://localhost:5001/signout-callback-oidc`
   - Production login example: `https://assistant.example.com/signin-oidc`
   - Production post-logout example: `https://assistant.example.com/signout-callback-oidc`
   Set local and production front-channel logout URLs respectively to `https://localhost:5001/signout-oidc` and `https://assistant.example.com/signout-oidc`. Replace the example host with the actual company host. The login callback (`/signin-oidc`), post-logout callback (`/signout-callback-oidc`) and front-channel logout URL (`/signout-oidc`) must remain distinct.
3. Under **Certificates & secrets**, create a short-lived client secret for the local test registration and record its value once. Do not put it in `appsettings.json`, `.env.example`, source control, tickets or logs. A production deployment should inject a separately managed secret and rotate it before expiry.
4. Under **App roles**, create these roles with **Users/Groups** as the allowed member type. The values are case-sensitive:

   | App-role value | Named permission granted by the backend |
   | --- | --- |
   | `Employee` | `assistant.use` |
   | `HR` | `hr.data.read` |
   | `TechnicalAdmin` | `technical.administration` |

   There is no inheritance. In particular, `TechnicalAdmin` does not grant `hr.data.read` or `assistant.use`. No HR data endpoint exists in this delivery; the mapping is enforced for future resource handlers.
5. Under **Enterprise applications**, select this application, open **Users and groups**, and assign test users or groups to explicit app roles. Under **Properties**, set **Assignment required?** to **Yes**. A user without a recognized role is rejected even if Entra completes authentication. No Graph API permission or exposed scope is needed for this sign-in-only flow.

## Configure and run locally

Use .NET user secrets for real local values:

```powershell
dotnet user-secrets init --project src/CompanyAssistant.Web
dotnet user-secrets set "AzureAd:TenantId" "<test-tenant-guid>" --project src/CompanyAssistant.Web
dotnet user-secrets set "AzureAd:ClientId" "<test-application-guid>" --project src/CompanyAssistant.Web
dotnet user-secrets set "AzureAd:ClientSecret" "<test-client-secret-value>" --project src/CompanyAssistant.Web
dotnet dev-certs https --trust
dotnet run --project src/CompanyAssistant.Web --urls "https://localhost:5001"
```

Open `https://localhost:5001`. Missing/empty/non-GUID tenant or client IDs, a missing secret, a non-HTTPS authority, or changed callback paths stop startup. Startup cannot prove that a syntactically valid secret is active; Entra rejects an expired or incorrect credential during the real code exchange.

The Compose configuration accepts `ENTRA_TENANT_ID`, `ENTRA_CLIENT_ID` and `ENTRA_CLIENT_SECRET` through `.env`. Its fictional placeholder IDs only permit anonymous smoke tests. Secure cookies require HTTPS, so use the HTTPS `dotnet run` flow above for real sign-in rather than `http://localhost:8080`.

## Manual real-tenant verification

These steps require real test-tenant credentials and are not performed by deterministic CI:

1. Before sign-in, open `/api/session` and verify `isAuthenticated` is false. Confirm `/api/protected` returns 401 without an OIDC redirect.
2. Sign in as an assigned `Employee`. Verify the session shows only `Employee` and `assistant.use`, and `/api/protected` returns 200 with chat disabled.
3. Repeat with `HR` and `TechnicalAdmin` users. Verify each session shows only its mapped permission and `/api/protected` returns 403. A user with multiple explicitly assigned roles receives the union of their permissions, not inheritance.
4. Verify an unassigned user cannot establish an application session. If possible, attempt a user from another tenant; the single-tenant authority/issuer validation must reject it. Wrong-audience validation is covered by a local signed-JWT test, not a live Entra check.
5. Attempt login with absolute, protocol-relative, backslash and encoded return URLs. Verify navigation remains on the application. Confirm an anonymous cross-site POST cannot log out; the UI logout uses a same-origin POST form with its CSRF token.
6. Select **Sign out** and follow the Entra redirect back to `/`. Verify `/api/session` is anonymous and the protected endpoint returns 401. Entra may still offer an account chooser on later sign-in depending on tenant policy.
7. Inspect browser storage and responses. The session cookie must be HTTP-only and Secure; no OIDC token may appear in localStorage, JSON or application logs. The CSRF request-token cookie is not a bearer credential.

## Production session, keys and proxy

Inject configuration through the deployment secret/configuration system:

- `AzureAd__Instance=https://login.microsoftonline.com/`
- `AzureAd__TenantId`, `AzureAd__ClientId` and `AzureAd__ClientSecret`
- `AzureAd__CallbackPath=/signin-oidc`, `AzureAd__SignedOutCallbackPath=/signout-callback-oidc` and `AzureAd__RemoteSignOutPath=/signout-oidc`
- `DataProtection__KeyPath=/var/lib/company-assistant/dataprotection`
- `ForwardedHeaders__KnownProxies__0=<exact-reverse-proxy-ip>` for each trusted proxy

Use one stable Data Protection key repository shared by all replicas of the one-company deployment. Persist it on encrypted durable storage, restrict read/write/create permission to the non-root application identity, protect backups and rotate keys according to the organization’s key-management procedure. Temporary or replica-local keys invalidate sessions and antiforgery cookies after restart or failover. The application name is fixed to `CompanyAssistant` so all replicas derive the same application-specific keys.

Terminate TLS at the trusted reverse proxy and forward `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`. The middleware processes one proxy hop and accepts forwarded headers only from its default loopback proxies or explicitly configured proxy IPs. Never clear the known-proxy/network lists to trust every source. Configure the proxy to validate the public Host header and set ASP.NET Core `AllowedHosts` appropriately. Register every externally visible HTTPS callback exactly in Entra.

## Deterministic test boundary

`tests/CompanyAssistant.Web.Tests` registers its header-based identity handler only in the test factory. It tests anonymous/role/permission access, invalid tenant/object claims, unassigned users, CSRF, safe return URLs and cookie settings. Separately, locally signed fictional JWTs exercise the configured audience and issuer validation. Static OIDC metadata prevents network discovery in logout tests. These tests do not prove live Entra issuer metadata, credentials, role assignment or browser redirects; complete the manual procedure above with a real test tenant.
