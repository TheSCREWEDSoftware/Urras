using System.Text.Json;

namespace Urras.Core.Accounts;

// What the user does in the browser: open VerificationUrl and type UserCode
public sealed record DeviceCode(string DeviceCodeValue, string UserCode, string VerificationUrl, int IntervalSeconds,
                                DateTimeOffset ExpiresAt);

// A token from the browser sign-in; GitLab's expire, so they come with a refresh token and an expiry time
public sealed record OAuthGrant(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);

// OAuth device flow (RFC 8628): the user approves a code in the browser and Urras receives a token
public sealed class DeviceSignIn
{
    // Urras's registered app ids, public by design so they ship in the app; GitLab's is empty until registered
    public const string GITHUB_CLIENT_ID = "Ov23lioCGrtLNlpGcwJG";
    public const string GITLAB_CLIENT_ID = "";

    public const string GITHUB_SCOPES = "repo read:user notifications";
    public const string GITLAB_SCOPES = "api read_user";

    private const string GRANT = "urn:ietf:params:oauth:grant-type:device_code";
    private const string REFRESH_GRANT = "refresh_token";
    private const int SLOW_DOWN_SECONDS = 5;

    private readonly HttpClient _http;
    private readonly string _codeUrl;
    private readonly string _tokenUrl;
    private readonly string _clientId;
    private readonly string _scopes;

    public DeviceSignIn(HttpClient http, string codeUrl, string tokenUrl, string clientId, string scopes)
    {
        _http = http;
        _codeUrl = codeUrl;
        _tokenUrl = tokenUrl;
        _clientId = clientId;
        _scopes = scopes;
    }

    // The device flow for an account kind, or null when its client id isn't set (then a token is pasted instead)
    public static DeviceSignIn? For(AccountKind kind, string server, HttpClient http, string? clientIdOverride = null)
    {
        string site = server.TrimEnd('/');
        return kind switch
        {
            AccountKind.GitHub when Id(clientIdOverride, GITHUB_CLIENT_ID) is { } id =>
                new DeviceSignIn(http, "https://github.com/login/device/code",
                                 "https://github.com/login/oauth/access_token", id, GITHUB_SCOPES),
            AccountKind.GitHubEnterprise when Id(clientIdOverride, null) is { } id =>
                new DeviceSignIn(http, site + "/login/device/code", site + "/login/oauth/access_token", id,
                                 GITHUB_SCOPES),
            AccountKind.GitLab when Id(clientIdOverride, site.EndsWith("gitlab.com") ? GITLAB_CLIENT_ID : null) is
                { } id =>
                new DeviceSignIn(http, site + "/oauth/authorize_device", site + "/oauth/token", id, GITLAB_SCOPES),
            _ => null
        };
    }

    private static string? Id(string? chosen, string? builtIn)
    {
        string? id = string.IsNullOrWhiteSpace(chosen) ? builtIn : chosen.Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public async Task<DeviceCode> StartAsync()
    {
        using JsonDocument answer = await PostAsync(_codeUrl, new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["scope"] = _scopes
        });
        JsonElement root = answer.RootElement;
        if (!root.TryGetProperty("device_code", out JsonElement device))
            throw new PlatformException(Error(root) ?? "Browser sign-in isn't turned on for this app.");

        string url = Text(root, "verification_uri_complete") ?? Text(root, "verification_uri") ?? string.Empty;
        int interval = root.TryGetProperty("interval", out JsonElement every) ? every.GetInt32() : SLOW_DOWN_SECONDS;
        int expires = root.TryGetProperty("expires_in", out JsonElement life) ? life.GetInt32() : 900;
        return new DeviceCode(device.GetString() ?? string.Empty, Text(root, "user_code") ?? string.Empty, url,
                              interval, DateTimeOffset.UtcNow.AddSeconds(expires));
    }

    // Waits until the user approves in the browser; throws PlatformException when denied or expired
    public async Task<string> WaitForTokenAsync(DeviceCode code, CancellationToken cancellation)
    {
        return (await WaitForGrantAsync(code, cancellation)).AccessToken;
    }

    // A new token from a refresh token, before the old one expires; throws PlatformException when refused
    public async Task<OAuthGrant> RefreshAsync(string refreshToken)
    {
        using JsonDocument answer = await PostAsync(_tokenUrl, new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = REFRESH_GRANT,
            ["refresh_token"] = refreshToken
        });
        return Grant(answer.RootElement) ?? throw new PlatformException(Error(answer.RootElement) ??
                                                                        "The sign-in couldn't be renewed.");
    }

    // The token with its refresh token and expiry, when the platform gives them
    public async Task<OAuthGrant> WaitForGrantAsync(DeviceCode code, CancellationToken cancellation)
    {
        int interval = code.IntervalSeconds;
        while (DateTimeOffset.UtcNow < code.ExpiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellation);
            using JsonDocument answer = await PostAsync(_tokenUrl, new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["device_code"] = code.DeviceCodeValue,
                ["grant_type"] = GRANT
            });
            JsonElement root = answer.RootElement;
            if (Grant(root) is { } grant)
                return grant;

            switch (Text(root, "error"))
            {
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval += SLOW_DOWN_SECONDS;
                    break;
                case "access_denied":
                    throw new PlatformException("Sign-in was cancelled in the browser.");
                case "expired_token":
                    throw new PlatformException("The code expired; start again.");
                default:
                    throw new PlatformException(Error(root) ?? "Sign-in failed.");
            }
        }

        throw new PlatformException("The code expired; start again.");
    }

    private async Task<JsonDocument> PostAsync(string url, Dictionary<string, string> form)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("Urras/0.1");
        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            try
            {
                return JsonDocument.Parse(text.Length == 0 ? "{}" : text);
            }
            catch (JsonException)
            {
                // A web page instead of an answer: the server has no device sign-in at this address
                throw new PlatformException(new Uri(url).Host + " doesn't offer browser sign-in (answered " +
                                            (int)response.StatusCode + "); GitLab needs version 17.3 or later");
            }
        }
        catch (HttpRequestException exception)
        {
            throw new PlatformException("Couldn't reach " + new Uri(url).Host + ": " + exception.Message);
        }
    }

    private static OAuthGrant? Grant(JsonElement root)
    {
        if (Text(root, "access_token") is not { Length: > 0 } token)
            return null;
        DateTimeOffset? expires = root.TryGetProperty("expires_in", out JsonElement life) &&
                                  life.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.UtcNow.AddSeconds(life.GetInt32())
            : null;
        return new OAuthGrant(token, Text(root, "refresh_token"), expires);
    }

    // The platform's reason, with what to change for the mistakes registering an application usually makes
    private static string? Error(JsonElement root)
    {
        string? reason = Text(root, "error_description") ?? Text(root, "error");
        string? fix = Text(root, "error") switch
        {
            "invalid_client" => "check the application id; on GitLab the application must not be Confidential",
            "invalid_scope" => "on GitLab, tick the api and read_user scopes on the application",
            "unauthorized_client" => "on GitHub, tick Enable Device Flow on the OAuth app",
            _ => null
        };
        return fix == null ? reason : reason + " (" + fix + ")";
    }

    private static string? Text(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
