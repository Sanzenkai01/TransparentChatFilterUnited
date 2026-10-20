using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TransparentChatFilter;

internal sealed record TwitchCredentials(
    string Username,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);

internal sealed record TwitchDeviceAuthorization(
    string UserCode,
    Uri VerificationUri,
    int ExpiresIn,
    int PollInterval);

internal sealed class TwitchOAuthException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

internal sealed class TwitchOAuthClient
{
    internal const string ClientId = "7mox2f8l2mu54i5pirpz2w0lfa1gdg";
    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";
    private static readonly HttpClient HttpClient = new();

    public async Task<TwitchCredentials> AuthorizeAsync(
        IProgress<TwitchDeviceAuthorization> progress,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.PostAsync(
            "https://id.twitch.tv/oauth2/device",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scopes"] = "chat:read"
            }),
            cancellationToken).ConfigureAwait(false);
        using var deviceDocument = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        var device = deviceDocument.RootElement;
        var deviceCode = GetRequiredString(device, "device_code");
        var userCode = GetRequiredString(device, "user_code");
        var verificationUri = new Uri(GetRequiredString(device, "verification_uri"));
        var expiresIn = GetRequiredInt32(device, "expires_in");
        var interval = Math.Max(1, TryGetInt32(device, "interval") ?? 5);

        progress.Report(new TwitchDeviceAuthorization(userCode, verificationUri, expiresIn, interval));

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        while (DateTimeOffset.UtcNow < expiresAt)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);

            using var tokenResponse = await HttpClient.PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["device_code"] = deviceCode,
                    ["scope"] = "chat:read",
                    ["grant_type"] = DeviceCodeGrantType
                }),
                cancellationToken).ConfigureAwait(false);
            var tokenResponseText = await tokenResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                using var errorDocument = JsonDocument.Parse(tokenResponseText);
                var errorCode = TryGetString(errorDocument.RootElement, "error") ??
                                TryGetString(errorDocument.RootElement, "message") ??
                                TryGetString(errorDocument.RootElement, "error_description");
                var normalizedErrorCode = errorCode?.Trim().ToLowerInvariant();
                switch (normalizedErrorCode)
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        interval += 5;
                        continue;
                    case "access_denied":
                        throw new InvalidOperationException("A autorização Twitch foi recusada pelo usuário.");
                    case "expired_token":
                        throw new InvalidOperationException("O código de autorização Twitch expirou. Tente conectar novamente.");
                    default:
                        var message = TryGetString(errorDocument.RootElement, "message") ??
                                      TryGetString(errorDocument.RootElement, "error_description") ??
                                      TryGetString(errorDocument.RootElement, "error") ??
                                      $"Erro HTTP {(int)tokenResponse.StatusCode} ao comunicar com a Twitch.";
                        throw new TwitchOAuthException(tokenResponse.StatusCode, message);
                }
            }

            using var tokenDocument = await ReadResponseAsync(tokenResponse, cancellationToken, tokenResponseText).ConfigureAwait(false);
            var token = tokenDocument.RootElement;
            var accessToken = GetRequiredString(token, "access_token");
            var refreshToken = GetRequiredString(token, "refresh_token");
            var tokenExpiresIn = GetRequiredInt32(token, "expires_in");
            var credentials = new TwitchCredentials(
                Username: string.Empty,
                AccessToken: accessToken,
                RefreshToken: refreshToken,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(tokenExpiresIn));
            return await AddUsernameAsync(credentials, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("O código de autorização Twitch expirou. Tente conectar novamente.");
    }

    public async Task<TwitchCredentials> ValidateOrRefreshAsync(
        TwitchCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(credentials.RefreshToken) &&
            credentials.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddMinutes(2))
        {
            credentials = await RefreshAsync(credentials, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await AddUsernameAsync(credentials, cancellationToken).ConfigureAwait(false);
        }
        catch (TwitchOAuthException ex) when (
            ex.StatusCode == HttpStatusCode.Unauthorized &&
            !string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            var refreshed = await RefreshAsync(credentials, cancellationToken).ConfigureAwait(false);
            return await AddUsernameAsync(refreshed, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<TwitchCredentials> RefreshAsync(TwitchCredentials credentials, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.PostAsync(
            "https://id.twitch.tv/oauth2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = credentials.RefreshToken
            }),
            cancellationToken).ConfigureAwait(false);
        using var document = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        var token = document.RootElement;
        return new TwitchCredentials(
            credentials.Username,
            GetRequiredString(token, "access_token"),
            TryGetString(token, "refresh_token") ?? credentials.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(GetRequiredInt32(token, "expires_in")));
    }

    private async Task<TwitchCredentials> AddUsernameAsync(TwitchCredentials credentials, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://id.twitch.tv/oauth2/validate");
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", credentials.AccessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        using var document = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        var username = GetRequiredString(document.RootElement, "login");
        return credentials with { Username = username };
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        string? responseText = null)
    {
        responseText ??= await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseText);
        }
        catch (JsonException ex)
        {
            throw new TwitchOAuthException(
                response.StatusCode,
                response.IsSuccessStatusCode
                    ? $"A Twitch retornou uma resposta inválida: {ex.Message}"
                    : $"Erro HTTP {(int)response.StatusCode} ao comunicar com a Twitch.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = TryGetString(document.RootElement, "message") ?? TryGetString(document.RootElement, "error");
            document.Dispose();
            throw new TwitchOAuthException(response.StatusCode, error ?? $"Erro HTTP {(int)response.StatusCode} ao comunicar com a Twitch.");
        }

        return document;
    }

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        TryGetString(element, propertyName)
        ?? throw new InvalidOperationException($"A resposta de autenticação Twitch não contém '{propertyName}'.");

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetRequiredInt32(JsonElement element, string propertyName) =>
        TryGetInt32(element, propertyName)
        ?? throw new InvalidOperationException($"A resposta de autenticação Twitch não contém '{propertyName}'.");

    private static int? TryGetInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

}
