using System.Net.Http;
using System.Text.Json;

namespace TransparentChatFilter;

internal sealed class TwitchChannelClient
{
    private static readonly HttpClient HttpClient = new();
    private static readonly SemaphoreSlim ProfileCacheLock = new(1, 1);
    private static readonly Dictionary<string, (DateTimeOffset ExpiresAt, string? ImageUrl)> ProfileImageCache =
        new(StringComparer.Ordinal);

    public async Task<string?> GetProfileImageUrlAsync(
        string channelId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            return null;
        }

        await ProfileCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ProfileImageCache.TryGetValue(channelId, out var cached) &&
                cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return cached.ImageUrl;
            }
        }
        finally
        {
            ProfileCacheLock.Release();
        }

        var imageUrl = await FetchProfileImageUrlAsync(channelId, accessToken, cancellationToken).ConfigureAwait(false);

        await ProfileCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ProfileImageCache[channelId] = (DateTimeOffset.UtcNow.AddHours(6), imageUrl);
            return imageUrl;
        }
        finally
        {
            ProfileCacheLock.Release();
        }
    }

    private static async Task<string?> FetchProfileImageUrlAsync(
        string channelId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var requestUri = $"https://api.twitch.tv/helix/users?id={Uri.EscapeDataString(channelId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("Client-Id", TwitchOAuthClient.ClientId);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("data", out var users) ||
            users.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A resposta Helix da Twitch não contém a lista data de canais.");
        }

        var user = users.EnumerateArray().FirstOrDefault();
        if (user.ValueKind == JsonValueKind.Undefined ||
            !user.TryGetProperty("profile_image_url", out var imageElement) ||
            imageElement.ValueKind != JsonValueKind.String)
        {
            if (user.ValueKind == JsonValueKind.Undefined)
            {
                return null;
            }

            throw new JsonException("A resposta Helix da Twitch não contém uma foto de perfil válida para o canal.");
        }

        var imageUrl = imageElement.GetString();
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var imageUri) ||
            imageUri.Scheme != Uri.UriSchemeHttps ||
            !(imageUri.Host.EndsWith(".jtvnw.net", StringComparison.OrdinalIgnoreCase) ||
              imageUri.Host.EndsWith(".twitch.tv", StringComparison.OrdinalIgnoreCase)))
        {
            throw new JsonException("A Twitch retornou um endereço inválido para a foto do canal.");
        }

        return imageUri.AbsoluteUri;
    }
}
