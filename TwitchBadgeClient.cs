using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace TransparentChatFilter;

internal sealed class TwitchBadgeClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private static readonly BoundedAsyncCache<string, IReadOnlyDictionary<string, TwitchChatBadge>> BadgeCache =
        new(256, StringComparer.Ordinal, TimeSpan.FromHours(6));
    private long _lastErrorReportedUtcTicks;

    public async Task<IReadOnlyList<TwitchChatBadge>> ResolveBadgesAsync(
        IReadOnlyList<string> roomIds,
        IReadOnlyList<TwitchBadgeReference> badgeReferences,
        string accessToken,
        Action<string> reportError,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, TwitchChatBadge> globalBadges =
            new Dictionary<string, TwitchChatBadge>(StringComparer.OrdinalIgnoreCase);
        try
        {
            globalBadges = await GetBadgesAsync("global", accessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBadgeRequestFailure(ex))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            ReportErrorOncePerMinute(reportError, $"Não foi possível carregar os badges globais da Twitch: {ex.Message}");
        }

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
            catch (Exception ex) when (IsBadgeRequestFailure(ex))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                ReportErrorOncePerMinute(
                    reportError,
                    $"Não foi possível carregar os badges específicos do canal: {ex.Message}");
                channelBadgeSets.Add(new Dictionary<string, TwitchChatBadge>(StringComparer.OrdinalIgnoreCase));
            }
        }

        var resolved = new List<TwitchChatBadge>(badgeReferences.Count);
        foreach (var reference in badgeReferences)
        {
            var key = $"{reference.SetId}/{reference.Version}";
            TwitchChatBadge? channelBadge = null;
            foreach (var channelBadges in channelBadgeSets)
            {
                if (channelBadges.TryGetValue(key, out channelBadge))
                {
                    break;
                }
            }

            if (channelBadge is not null)
            {
                resolved.Add(channelBadge);
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

    private void ReportErrorOncePerMinute(Action<string> reportError, string message)
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        while (true)
        {
            var lastReported = Volatile.Read(ref _lastErrorReportedUtcTicks);
            if (lastReported != 0 && now - lastReported < TimeSpan.FromMinutes(1).Ticks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastErrorReportedUtcTicks, now, lastReported) == lastReported)
            {
                reportError(message);
                return;
            }
        }
    }

    private static bool IsBadgeRequestFailure(Exception exception) =>
        exception is HttpRequestException or JsonException or IOException or OperationCanceledException or
            InvalidOperationException or FormatException or ArgumentException;

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

    private static Task<IReadOnlyDictionary<string, TwitchChatBadge>> GetBadgesAsync(
        string badgeScope,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var requestUri = badgeScope == "global"
            ? "https://api.twitch.tv/helix/chat/badges/global"
            : $"https://api.twitch.tv/helix/chat/badges?broadcaster_id={Uri.EscapeDataString(badgeScope)}";
        return BadgeCache.GetOrCreateAsync(
            badgeScope,
            () => FetchBadgesWithRetryAsync(requestUri, accessToken),
            cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, TwitchChatBadge>> FetchBadgesWithRetryAsync(
        string requestUri,
        string accessToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await FetchBadgesAsync(requestUri, accessToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                attempt == 0 &&
                (ex is TaskCanceledException or IOException ||
                 ex is HttpRequestException { StatusCode: not HttpStatusCode.Unauthorized }))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350)).ConfigureAwait(false);
            }
        }
    }

    private static async Task<IReadOnlyDictionary<string, TwitchChatBadge>> FetchBadgesAsync(
        string requestUri,
        string accessToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("Client-Id", TwitchOAuthClient.ClientId);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token).ConfigureAwait(false);

        return ParseBadgeSets(document.RootElement);
    }

    internal static IReadOnlyDictionary<string, TwitchChatBadge> ParseBadgeSets(JsonElement root)
    {
        var badges = new Dictionary<string, TwitchChatBadge>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("data", out var badgeSets) ||
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
                    !TryGetImageUrl(version, "image_url_1x", out var imageUrl1x))
                {
                    continue;
                }

                var title = version.TryGetProperty("title", out var titleElement)
                    ? titleElement.GetString()
                    : null;
                var imageUrl2x = TryGetImageUrl(version, "image_url_2x", out var imageUrl)
                    ? imageUrl
                    : imageUrl1x;
                var imageUrl4x = TryGetImageUrl(version, "image_url_4x", out imageUrl)
                    ? imageUrl
                    : imageUrl2x;
                var badge = new TwitchChatBadge(
                    setIdElement.GetString()!,
                    versionIdElement.GetString()!,
                    string.IsNullOrWhiteSpace(title) ? setIdElement.GetString()! : title,
                    imageUrl1x,
                    imageUrl2x,
                    imageUrl4x);
                badges[$"{badge.SetId}/{badge.Version}"] = badge;
            }
        }

        return badges;
    }

    private static bool TryGetImageUrl(JsonElement version, string propertyName, out string imageUrl)
    {
        imageUrl = string.Empty;
        return version.TryGetProperty(propertyName, out var imageUrlElement) &&
               !string.IsNullOrWhiteSpace(imageUrl = imageUrlElement.GetString() ?? string.Empty);
    }
}
