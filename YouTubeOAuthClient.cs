using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransparentChatFilter;

internal sealed record YouTubeCredentials(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);

internal sealed class YouTubeOAuthClient
{
    internal const string ClientId = "260004824920-r04kojb2hu08pidhr959jejc9esmvgta.apps.googleusercontent.com";
    private const string Scope = "https://www.googleapis.com/auth/youtube.readonly";
    private static readonly HttpClient HttpClient = new();

    public async Task<YouTubeCredentials> AuthorizeAsync(CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirectUri = $"http://127.0.0.1:{port}/";
        var verifier = CreateBase64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = CreateBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = CreateBase64Url(RandomNumberGenerator.GetBytes(32));
        var authorizationUri = new Uri(
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            "&access_type=offline&prompt=consent" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&code_challenge_method=S256" +
            $"&state={Uri.EscapeDataString(state)}");

        try
        {
            Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"Não foi possível abrir o navegador para autorizar o YouTube: {ex.Message}", ex);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));
        TcpClient client;
        try
        {
            client = await listener.AcceptTcpClientAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "O Google não retornou a autorização ao aplicativo. Confira se o Client ID pertence a um cliente OAuth do tipo Aplicativo para computador (não Aplicativo da Web) e tente novamente.");
        }

        using (client)
        {
            var (code, returnedState) = await ReadAuthorizationCallbackAsync(client, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state),
                    Encoding.UTF8.GetBytes(returnedState)))
            {
                throw new InvalidOperationException("A resposta de autorização do Google não corresponde à solicitação iniciada.");
            }

            using var response = await HttpClient.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["code"] = code,
                    ["code_verifier"] = verifier,
                    ["redirect_uri"] = redirectUri,
                    ["grant_type"] = "authorization_code"
                }),
                cancellationToken).ConfigureAwait(false);
            using var document = await ReadJsonResponseAsync(response, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            var refreshToken = GetString(root, "refresh_token");
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new InvalidOperationException("O Google não retornou um token de renovação. Revogue o acesso do app nas configurações da conta Google e tente autorizar novamente.");
            }

            return new YouTubeCredentials(
                GetString(root, "access_token"),
                refreshToken,
                DateTimeOffset.UtcNow.AddSeconds(GetInt32(root, "expires_in")));
        }
    }

    public async Task<YouTubeCredentials> RefreshAsync(
        YouTubeCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.PostAsync(
            "https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["refresh_token"] = credentials.RefreshToken,
                ["grant_type"] = "refresh_token"
            }),
            cancellationToken).ConfigureAwait(false);
        using var document = await ReadJsonResponseAsync(response, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        return new YouTubeCredentials(
            GetString(root, "access_token"),
            credentials.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(GetInt32(root, "expires_in")));
    }

    private static async Task<(string Code, string State)> ReadAuthorizationCallbackAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)))
        {
        }

        var target = requestLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
        var query = target is null
            ? new Dictionary<string, string>()
            : ParseQuery(new Uri("http://127.0.0.1" + target).Query);
        var code = query.GetValueOrDefault("code");
        var state = query.GetValueOrDefault("state");
        var error = query.GetValueOrDefault("error");
        var page = string.IsNullOrWhiteSpace(code)
            ? "Authorization was not completed. You may close this tab and try again."
            : "YouTube authorization complete. You may close this tab and return to the chat.";
        var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=utf-8><title>YouTube</title><p>{WebUtility.HtmlEncode(page)}</p>");
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException($"A autorização do Google foi recusada ou não foi concluída ({error}).");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            throw new InvalidOperationException("O Google retornou uma resposta de autorização incompleta.");
        }

        return (code, state);
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
                pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')),
                StringComparer.Ordinal);

    private static async Task<JsonDocument> ReadJsonResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Resposta inválida do Google (HTTP {(int)response.StatusCode}): {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = document.RootElement.TryGetProperty("error_description", out var description)
                ? description.GetString()
                : document.RootElement.TryGetProperty("error", out var code)
                    ? code.GetString()
                    : null;
            document.Dispose();
            throw new InvalidOperationException(error ?? $"Erro HTTP {(int)response.StatusCode} na autorização do Google.");
        }

        return document;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidOperationException($"A resposta do Google não contém '{propertyName}'.")
            : throw new InvalidOperationException($"A resposta do Google não contém '{propertyName}'.");

    private static int GetInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"A resposta do Google não contém '{propertyName}'.");

    private static string CreateBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
