using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TransparentChatFilter;

internal sealed class YouTubeChatClient
{
    private static readonly HttpClient HttpClient = new();
    private CancellationTokenSource? _connectionCts;
    private Task? _pollTask;
    private volatile bool _isConnected;

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ErrorReported;

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(
        string videoId,
        Func<CancellationToken, Task<string>> accessTokenProvider,
        CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var accessToken = await accessTokenProvider(cancellationToken).ConfigureAwait(false);
        var liveChatId = await GetLiveChatIdAsync(videoId, accessToken, cancellationToken).ConfigureAwait(false);
        var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectionCts = connectionCts;
        _isConnected = true;
        StatusChanged?.Invoke(this, "YouTube conectado");
        _pollTask = PollAsync(videoId, liveChatId, accessTokenProvider, connectionCts.Token);
    }

    public async Task DisconnectAsync()
    {
        var cts = _connectionCts;
        var pollTask = _pollTask;
        _connectionCts = null;
        _pollTask = null;
        _isConnected = false;
        cts?.Cancel();
        if (pollTask is not null)
        {
            try
            {
                await pollTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts?.Dispose();
    }

    private async Task<string> GetLiveChatIdAsync(
        string videoId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var uri = $"https://www.googleapis.com/youtube/v3/videos?part=liveStreamingDetails&id={Uri.EscapeDataString(videoId)}";
        using var document = await SendApiRequestAsync(uri, accessToken, cancellationToken).ConfigureAwait(false);
        var items = document.RootElement.GetProperty("items");
        var video = items.EnumerateArray().FirstOrDefault();
        if (video.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("O YouTube não encontrou esse vídeo. Confira o ID e tente novamente.");
        }

        if (!video.TryGetProperty("liveStreamingDetails", out var liveDetails) ||
            !liveDetails.TryGetProperty("activeLiveChatId", out var liveChatId) ||
            string.IsNullOrWhiteSpace(liveChatId.GetString()))
        {
            throw new InvalidOperationException("Esse vídeo não tem um chat ao vivo ativo. Verifique se a transmissão está ao vivo e se o chat está ativado.");
        }

        return liveChatId.GetString()!;
    }

    private async Task PollAsync(
        string videoId,
        string liveChatId,
        Func<CancellationToken, Task<string>> accessTokenProvider,
        CancellationToken cancellationToken)
    {
        string? pageToken = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var accessToken = await accessTokenProvider(cancellationToken).ConfigureAwait(false);
                var query = $"https://www.googleapis.com/youtube/v3/liveChat/messages?part=snippet,authorDetails&liveChatId={Uri.EscapeDataString(liveChatId)}&maxResults=200";
                if (!string.IsNullOrWhiteSpace(pageToken))
                {
                    query += $"&pageToken={Uri.EscapeDataString(pageToken)}";
                }

                using var document = await SendApiRequestAsync(query, accessToken, cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                foreach (var item in root.GetProperty("items").EnumerateArray())
                {
                    var snippet = item.GetProperty("snippet");
                    var author = item.GetProperty("authorDetails");
                    var displayName = author.TryGetProperty("displayName", out var displayNameValue)
                        ? displayNameValue.GetString() ?? "YouTube"
                        : "YouTube";
                    var text = snippet.TryGetProperty("displayMessage", out var messageValue)
                        ? messageValue.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var timestamp = snippet.TryGetProperty("publishedAt", out var publishedAt) &&
                                    DateTimeOffset.TryParse(publishedAt.GetString(), out var parsedTimestamp)
                        ? parsedTimestamp
                        : DateTimeOffset.UtcNow;
                    MessageReceived?.Invoke(this, new ChatMessage
                    {
                        Platform = ChatPlatform.YouTube,
                        DisplayName = displayName,
                        LoginName = displayName,
                        Message = text,
                        Channel = videoId,
                        OriginChannel = videoId,
                        SourceRoomId = liveChatId,
                        TimestampUtc = timestamp
                    });
                }

                pageToken = root.TryGetProperty("nextPageToken", out var nextPageToken)
                    ? nextPageToken.GetString()
                    : null;
                var delayMilliseconds = root.TryGetProperty("pollingIntervalMillis", out var pollingInterval) &&
                                       pollingInterval.TryGetInt32(out var interval)
                    ? Math.Max(interval, 1000)
                    : 5000;
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _isConnected = false;
            StatusChanged?.Invoke(this, "YouTube desconectado");
            ErrorReported?.Invoke(this, $"A conexão com o chat do YouTube foi interrompida: {ex.Message}");
            return;
        }

        _isConnected = false;
        StatusChanged?.Invoke(this, "YouTube desconectado");
    }

    private static async Task<JsonDocument> SendApiRequestAsync(
        string uri,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"O YouTube retornou uma resposta inválida (HTTP {(int)response.StatusCode}): {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = document.RootElement.TryGetProperty("error", out var error) &&
                               error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
            document.Dispose();
            throw new InvalidOperationException(errorMessage ?? $"Erro HTTP {(int)response.StatusCode} ao acessar o YouTube.");
        }

        return document;
    }
}
