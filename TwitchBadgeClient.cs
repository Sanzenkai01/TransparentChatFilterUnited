using System.Net.Http;
using System.Text.Json;

namespace TransparentChatFilter;

internal sealed class TwitchBadgeClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };
    private static readonly SemaphoreSlim BadgeCacheLock = new(1, 1);
    private static readonly Dictionary<string, (DateTimeOffset ExpiresAt, Task<IReadOnlyDictionary<string, TwitchChatBadge>> Badges)> BadgeCache =
        new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<TwitchChatBadge>> ResolveBadgesAsync(
        IReadOnlyList<string> roomIds,
        IReadOnlyList<TwitchBadgeReference> badgeReferences,
        string accessToken,
        Action<string> reportError,
        CancellationToken cancellationToken)
    {
        var globalBadges = await GetBadgesAsync("global", accessToken, cancellationToken).ConfigureAwait(false);
        var channelBadgeSets = new List<IReadOnlyDictionary<string, TwitchChatBadge>>();
        var uniqueRoomIds = roomIds
            .Where(roomId => !string.IsNullOrWhiteSpace(roomId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var roomId in uniqueRoomIds)
        {
            try
            {
                channelBadgeSets.Add(await GetBadgesAsync(roomId, accessToken, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (
                ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                reportError($"Não foi possível carregar os badges específicos do canal: {ex.Message}");
            }
        }

        var resolved = new List<TwitchChatBadge>(badgeReferences.Count);
        foreach (var reference in badgeReferences)
        {
            var key = $"{reference.SetId}/{reference.Version}";
            var channelBadge = channelBadgeSets
                .SelectMany(badges => badges)
                .FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(channelBadge.Key))
            {
                resolved.Add(channelBadge.Value);
            }
            else if (globalBadges.TryGetValue(key, out var globalBadge))
            {
                resolved.Add(globalBadge);
            }
            else
            {
                resolved.Add(new TwitchChatBadge(
                    reference.SetId,
                    reference.Version,
                    GetFallbackTitle(reference.SetId),
                    string.Empty));
            }
        }

        return resolved;
    }

    private static string GetFallbackTitle(string setId)
    {
        return setId.ToLowerInvariant() switch
        {
            "moderator" => "Moderador",
            "vip" => "VIP",
            "subscriber" => "Inscrito",
            "broadcaster" => "Transmissor",
            "staff" => "Equipe Twitch",
            "admin" => "Administrador Twitch",
            "global_mod" => "Moderador global",
            _ => setId
        };
    }

    private static async Task<IReadOnlyDictionary<string, TwitchChatBadge>> GetBadgesAsync(
        string badgeScope,
        string accessToken,
        CancellationToken cancellationToken)
    {
        Task<IReadOnlyDictionary<string, TwitchChatBadge>> badgesTask;
        await BadgeCacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (BadgeCache.TryGetValue(badgeScope, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                badgesTask = cached.Badges;
            }
            else
            {
                var requestUri = badgeScope == "global"
                    ? "https://api.twitch.tv/helix/chat/badges/global"
                    : $"https://api.twitch.tv/helix/chat/badges?broadcaster_id={Uri.EscapeDataString(badgeScope)}";
                badgesTask = FetchBadgesWithRetryAsync(requestUri, accessToken);
                BadgeCache[badgeScope] = (DateTimeOffset.UtcNow.AddHours(6), badgesTask);
            }
        }
        finally
        {
            BadgeCacheLock.Release();
        }

        try
        {
            return await badgesTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (badgesTask.IsFaulted || badgesTask.IsCanceled)
        {
            await BadgeCacheLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (BadgeCache.TryGetValue(badgeScope, out var cached) &&
                    ReferenceEquals(cached.Badges, badgesTask))
                {
                    BadgeCache.Remove(badgeScope);
                }
            }
            finally
            {
                BadgeCacheLock.Release();
            }

            throw;
        }
    }

    private static async Task<IReadOnlyDictionary<string, TwitchChatBadge>> FetchBadgesWithRetryAsync(
        string requestUri,
        string accessToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await FetchBadgesAsync(requestUri, accessToken, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                attempt == 0 &&
                (ex is HttpRequestException or TaskCanceledException))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350)).ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyDictionary<string, TwitchChatBadge>> FetchBadgesAsync(
        string requestUri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("Client-Id", TwitchOAuthClient.ClientId);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var badges = new Dictionary<string, TwitchChatBadge>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("data", out var badgeSets) ||
            badgeSets.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A resposta Helix da Twitch não contém a lista data de badges.");
        }

        foreach (var badgeSet in badgeSets.EnumerateArray())
        {
            if (!badgeSet.TryGetProperty("set_id", out var setIdElement) ||
                string.IsNullOrWhiteSpace(setIdElement.GetString()) ||
                !badgeSet.TryGetProperty("versions", out var versions) ||
                versions.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var version in versions.EnumerateArray())
            {
                if (!version.TryGetProperty("id", out var versionIdElement) ||
                    string.IsNullOrWhiteSpace(versionIdElement.GetString()) ||
                    !version.TryGetProperty("image_url_1x", out var imageUrlElement) ||
                    string.IsNullOrWhiteSpace(imageUrlElement.GetString()))
                {
                    continue;
                }

                var title = version.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString()
                    : null;
                var badge = new TwitchChatBadge(
                    setIdElement.GetString()!,
                    versionIdElement.GetString()!,
                    string.IsNullOrWhiteSpace(title) ? setIdElement.GetString()! : title,
                    imageUrlElement.GetString()!);
                badges[$"{badge.SetId}/{badge.Version}"] = badge;
            }
        }

        return badges;
    }
}
