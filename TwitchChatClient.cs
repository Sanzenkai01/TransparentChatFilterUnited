using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TransparentChatFilter;

public enum ChatMessageKind
{
    Chat,
    Subscription,
    Resubscription,
    GiftSubscription,
    Raid,
    Bits,
    Announcement
}

public sealed record TwitchEmoteRange(string Id, int Start, int End);
public sealed record TwitchBadgeReference(string SetId, string Version);
public sealed record TwitchChatBadge(string SetId, string Version, string Title, string ImageUrl);
public sealed record TwitchChatClear(string Channel, string? Username);

public sealed class ChatMessage
{
    public string DisplayName { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string OriginChannel { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string SourceRoomId { get; set; } = string.Empty;
    public string ChannelImageUrl { get; set; } = string.Empty;
    public string RawMetadata { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string SystemText { get; set; } = string.Empty;
    public ChatMessageKind Kind { get; set; }
    public List<TwitchEmoteRange> Emotes { get; set; } = new();
    public List<TwitchBadgeReference> BadgeReferences { get; set; } = new();
    public List<TwitchChatBadge> Badges { get; set; } = new();
    public bool IsModerator { get; set; }
    public bool IsVip { get; set; }
    public bool IsSubscriber { get; set; }
    public bool IsBroadcaster { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public string EffectiveOrigin => string.IsNullOrWhiteSpace(OriginChannel) ? Channel : OriginChannel;
    public bool HasKnownOrigin => !string.IsNullOrWhiteSpace(OriginChannel) || !string.IsNullOrWhiteSpace(SourceRoomId);
}

public sealed class TwitchChatClient : IDisposable
{
    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<string>? MessageDeleted;
    public event EventHandler<TwitchChatClear>? UserMessagesCleared;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? ErrorReported;

    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _connectionCts;
    private Task? _readLoopTask;
    private long _connectionGeneration;
    private volatile bool _isConnected;
    private volatile bool _disposed;

    public bool IsConnected => _isConnected;

    public async Task ConnectAsync(IEnumerable<string> channelNames, string username, string accessToken, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TwitchChatClient));
        }

        var channels = channelNames
            .Select(NormalizeChannel)
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (channels.Length == 0)
        {
            throw new InvalidOperationException("Informe pelo menos um canal para conectar.");
        }

        var normalizedUsername = NormalizeChannel(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new InvalidOperationException("Informe o nome de usuário da conta Twitch que emitiu o token.");
        }

        var normalizedToken = accessToken.StartsWith("oauth:", StringComparison.OrdinalIgnoreCase)
            ? accessToken[6..]
            : accessToken;
        if (string.IsNullOrWhiteSpace(normalizedToken) || normalizedToken.Contains('\r') || normalizedToken.Contains('\n'))
        {
            throw new InvalidOperationException("Informe um token de acesso Twitch válido.");
        }

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Disconnect();

            var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tcpClient = new TcpClient();
            long generation;
            lock (_syncRoot)
            {
                generation = ++_connectionGeneration;
                _connectionCts = connectionCts;
                _tcpClient = tcpClient;
            }

            try
            {
                await tcpClient.ConnectAsync("irc.chat.twitch.tv", 6697, connectionCts.Token).ConfigureAwait(false);
                var secureStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
                await secureStream.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions { TargetHost = "irc.chat.twitch.tv" },
                    connectionCts.Token).ConfigureAwait(false);
                var stream = secureStream;
                var reader = new StreamReader(stream, Encoding.UTF8);
                var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true, NewLine = "\r\n" };

                lock (_syncRoot)
                {
                    if (generation != _connectionGeneration || connectionCts.IsCancellationRequested)
                    {
                        reader.Dispose();
                        writer.Dispose();
                        throw new OperationCanceledException(connectionCts.Token);
                    }

                    _reader = reader;
                    _writer = writer;
                }

                writer.WriteLine($"PASS oauth:{normalizedToken}");
                writer.WriteLine($"NICK {normalizedUsername}");
                writer.WriteLine("CAP REQ :twitch.tv/tags twitch.tv/commands twitch.tv/membership");
                var authentication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _readLoopTask = Task.Run(() => ReadLoopAsync(reader, writer, connectionCts, generation, authentication));
                await authentication.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);

                foreach (var joinCommand in CreateJoinCommands(channels))
                {
                    writer.WriteLine(joinCommand);
                }

                _isConnected = true;
                StatusChanged?.Invoke(this, $"Conectado a {channels.Length} canal(is)");
            }
            catch
            {
                CloseConnection(generation);
                throw;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private static IEnumerable<string> CreateJoinCommands(IEnumerable<string> channels)
    {
        var batch = new List<string>();
        var length = "JOIN ".Length;
        foreach (var channel in channels)
        {
            var target = $"#{channel}";
            if (batch.Count > 0 && length + target.Length + 1 > 450)
            {
                yield return $"JOIN {string.Join(',', batch)}";
                batch.Clear();
                length = "JOIN ".Length;
            }

            batch.Add(target);
            length += target.Length + (batch.Count > 1 ? 1 : 0);
        }

        if (batch.Count > 0)
        {
            yield return $"JOIN {string.Join(',', batch)}";
        }
    }

    public void Disconnect()
    {
        CloseConnection();
    }

    private void CloseConnection(long? expectedGeneration = null)
    {
        TcpClient? tcpClient;
        StreamReader? reader;
        StreamWriter? writer;
        CancellationTokenSource? connectionCts;
        bool wasConnected;
        lock (_syncRoot)
        {
            if (expectedGeneration.HasValue && expectedGeneration.Value != _connectionGeneration)
            {
                return;
            }

            if (!expectedGeneration.HasValue)
            {
                _connectionGeneration++;
            }

            tcpClient = _tcpClient;
            reader = _reader;
            writer = _writer;
            connectionCts = _connectionCts;
            wasConnected = _isConnected;
            _writer = null;
            _reader = null;
            _tcpClient = null;
            _connectionCts = null;
            _isConnected = false;
        }

        connectionCts?.Cancel();
        writer?.Dispose();
        reader?.Dispose();
        tcpClient?.Dispose();
        connectionCts?.Dispose();
        if (wasConnected)
        {
            StatusChanged?.Invoke(this, "Desconectado");
        }
    }

    private async Task ReadLoopAsync(
        StreamReader reader,
        StreamWriter writer,
        CancellationTokenSource connectionCts,
        long generation,
        TaskCompletionSource authentication)
    {
        var cancellationToken = connectionCts.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("PING ", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteLine($"PONG {line[5..]}");
                    continue;
                }

                if (HasIrcCommand(line, "001"))
                {
                    authentication.TrySetResult();
                    continue;
                }

                if (HasIrcCommand(line, "NOTICE"))
                {
                    var notice = line.Contains(" :")
                        ? line[(line.LastIndexOf(" :", StringComparison.Ordinal) + 2)..]
                        : "A Twitch recusou a autenticação.";
                    if (authentication.TrySetException(new InvalidOperationException(
                            $"A Twitch recusou a conexão: {notice}. Confira usuário e token com permissão chat:read.")))
                    {
                        break;
                    }

                    StatusChanged?.Invoke(this, $"Aviso da Twitch: {notice}");
                    continue;
                }

                if (HasIrcCommand(line, "CLEARMSG"))
                {
                    var tags = ParseLineTags(line);
                    var messageId = tags.GetValueOrDefault("target-msg-id", string.Empty);
                    if (!string.IsNullOrWhiteSpace(messageId))
                    {
                        MessageDeleted?.Invoke(this, messageId);
                    }
                }
                else if (HasIrcCommand(line, "CLEARCHAT"))
                {
                    var payload = GetPayload(line);
                    var parts = payload.Split(' ', 4, StringSplitOptions.None);
                    if (parts.Length >= 3)
                    {
                        var channel = NormalizeChannel(parts[2]);
                        var targetUser = parts.Length >= 4 ? parts[3].TrimStart(':').Trim() : string.Empty;
                        if (!string.IsNullOrWhiteSpace(channel))
                        {
                            UserMessagesCleared?.Invoke(
                                this,
                                new TwitchChatClear(channel, string.IsNullOrWhiteSpace(targetUser) ? null : targetUser));
                        }
                    }
                }
                else if (HasIrcCommand(line, "PRIVMSG"))
                {
                    var message = ParsePrivMessage(line);
                    if (message is not null)
                    {
                        MessageReceived?.Invoke(this, message);
                    }
                }
                else if (HasIrcCommand(line, "USERNOTICE"))
                {
                    var message = ParseUserNotice(line);
                    if (message is not null)
                    {
                        MessageReceived?.Invoke(this, message);
                    }
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                var error = new IOException("A Twitch encerrou a conexão antes de confirmar a autenticação.");
                if (!authentication.TrySetException(error))
                {
                    ErrorReported?.Invoke(this, "Conexão encerrada pela Twitch.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            authentication.TrySetCanceled(cancellationToken);
            // expected during shutdown
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            // expected when the active connection is closed
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // expected when the active connection is closed
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!authentication.TrySetException(new IOException("Conexão encerrada durante a autenticação Twitch.")))
            {
                ErrorReported?.Invoke(this, "Conexão encerrada pela Twitch.");
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (!authentication.TrySetException(ex))
            {
                ErrorReported?.Invoke(this, ex.Message);
            }
        }
        finally
        {
            CloseConnection(generation);
        }
    }

    private static ChatMessage? ParsePrivMessage(string rawLine)
    {
        try
        {
            var tags = ParseLineTags(rawLine);
            var payload = GetPayload(rawLine);
            var parts = payload.Split(' ', 4, StringSplitOptions.None);
            if (parts.Length < 4)
            {
                return null;
            }

            var prefix = parts[0];
            var command = parts[1];
            var channelToken = parts[2];
            var messageText = parts[3].StartsWith(':') ? parts[3][1..] : parts[3];

            if (!string.Equals(command, "PRIVMSG", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var metadata = tags;
            var displayName = metadata.TryGetValue("display-name", out var name) ? name : "Unknown";
            var channelName = NormalizeChannel(channelToken);
            var isVip = metadata.TryGetValue("vip", out var vipValue) && string.Equals(vipValue, "1", StringComparison.Ordinal);
            var isMod = metadata.TryGetValue("mod", out var modValue) && string.Equals(modValue, "1", StringComparison.Ordinal);
            var isSubscriber = metadata.TryGetValue("subscriber", out var subValue) && string.Equals(subValue, "1", StringComparison.Ordinal);
            var isBroadcaster = string.Equals(
                metadata.GetValueOrDefault("room-id"),
                metadata.GetValueOrDefault("user-id"),
                StringComparison.Ordinal);
            var badgeReferences = ParseMessageBadgeReferences(metadata, isMod, isVip, isSubscriber, isBroadcaster);
            var bits = metadata.GetValueOrDefault("bits", string.Empty);
            var isCheer = int.TryParse(bits, out var bitCount) && bitCount > 0;
            var author = string.IsNullOrWhiteSpace(displayName) ? "Alguém" : displayName;

            return new ChatMessage
            {
                DisplayName = displayName,
                LoginName = prefix.StartsWith(':') && prefix.Contains('!')
                    ? prefix[1..prefix.IndexOf('!')]
                    : string.Empty,
                Message = messageText,
                Channel = channelName,
                OriginChannel = channelName,
                RoomId = metadata.GetValueOrDefault("room-id", string.Empty),
                SourceRoomId = metadata.GetValueOrDefault("source-room-id", string.Empty),
                RawMetadata = string.Join("; ", metadata.Select(item => $"{item.Key}={item.Value}")),
                MessageId = metadata.GetValueOrDefault("id", string.Empty),
                Emotes = ParseEmoteRanges(metadata.GetValueOrDefault("emotes", string.Empty)),
                BadgeReferences = badgeReferences,
                Kind = isCheer ? ChatMessageKind.Bits : ChatMessageKind.Chat,
                SystemText = isCheer ? $"{author} enviou {bitCount} Bits!" : string.Empty,
                IsVip = isVip,
                IsModerator = isMod,
                IsSubscriber = isSubscriber,
                IsBroadcaster = isBroadcaster,
                TimestampUtc = ParseTimestamp(metadata.GetValueOrDefault("tmi-sent-ts"))
            };
        }
        catch
        {
            return null;
        }
    }

    private static ChatMessage? ParseUserNotice(string rawLine)
    {
        try
        {
            var metadata = ParseLineTags(rawLine);
            var payload = GetPayload(rawLine);
            var parts = payload.Split(' ', 4, StringSplitOptions.None);
            if (parts.Length < 3)
            {
                return null;
            }

            var channel = NormalizeChannel(parts[2]);
            var displayName = metadata.GetValueOrDefault("display-name", string.Empty);
            var loginName = metadata.GetValueOrDefault("login", string.Empty);
            var eventId = metadata.GetValueOrDefault("msg-id", string.Empty);
            var comment = parts.Length == 4 ? parts[3].TrimStart(':') : string.Empty;
            var (kind, systemText) = CreateNoticeText(eventId, displayName, metadata);
            if (string.IsNullOrWhiteSpace(systemText))
            {
                return null;
            }

            return new ChatMessage
            {
                DisplayName = displayName,
                LoginName = loginName,
                Message = comment,
                SystemText = systemText,
                Kind = kind,
                Channel = channel,
                OriginChannel = channel,
                RoomId = metadata.GetValueOrDefault("room-id", string.Empty),
                SourceRoomId = metadata.GetValueOrDefault("source-room-id", string.Empty),
                RawMetadata = string.Join("; ", metadata.Select(item => $"{item.Key}={item.Value}")),
                MessageId = metadata.GetValueOrDefault("id", string.Empty),
                Emotes = ParseEmoteRanges(metadata.GetValueOrDefault("emotes", string.Empty)),
                BadgeReferences = ParseMessageBadgeReferences(
                    metadata,
                    metadata.GetValueOrDefault("mod") == "1",
                    metadata.GetValueOrDefault("vip") == "1",
                    metadata.GetValueOrDefault("subscriber") == "1",
                    metadata.GetValueOrDefault("room-id") == metadata.GetValueOrDefault("user-id")),
                IsModerator = metadata.GetValueOrDefault("mod") == "1",
                IsVip = metadata.GetValueOrDefault("vip") == "1",
                IsSubscriber = metadata.GetValueOrDefault("subscriber") == "1",
                IsBroadcaster = metadata.GetValueOrDefault("room-id") == metadata.GetValueOrDefault("user-id"),
                TimestampUtc = ParseTimestamp(metadata.GetValueOrDefault("tmi-sent-ts"))
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static (ChatMessageKind Kind, string Text) CreateNoticeText(
        string eventId,
        string displayName,
        IReadOnlyDictionary<string, string> metadata)
    {
        var author = string.IsNullOrWhiteSpace(displayName) ? "Alguém" : displayName;
        var recipient = metadata.GetValueOrDefault("msg-param-recipient-display-name", "alguém");
        var months = metadata.GetValueOrDefault("msg-param-months", string.Empty);
        var viewers = metadata.GetValueOrDefault("msg-param-viewerCount", string.Empty);
        var gifts = metadata.GetValueOrDefault("msg-param-mass-gift-count", string.Empty);
        var bits = metadata.GetValueOrDefault("bits", metadata.GetValueOrDefault("msg-param-amount", string.Empty));

        return eventId switch
        {
            "sub" =>
                (ChatMessageKind.Subscription, $"{author} se inscreveu no canal!"),
            "resub" =>
                (ChatMessageKind.Resubscription, $"{author} renovou a inscrição{FormatCount(months, " por {0} meses")}!"),
            "subgift" or "anonsubgift" =>
                (ChatMessageKind.GiftSubscription, $"{(eventId == "anonsubgift" ? "Alguém" : author)} presenteou {recipient} com uma inscrição!"),
            "submysterygift" or "anonsubmysterygift" =>
                (ChatMessageKind.GiftSubscription, $"{author} presenteou a comunidade{FormatCount(gifts, " com {0} inscrições")}!"),
            "raid" =>
                (ChatMessageKind.Raid, $"{author} chegou em raid{FormatCount(viewers, " com {0} espectadores")}!"),
            "bitsbadger" or "bitscharity" =>
                (ChatMessageKind.Bits, $"{author} enviou{FormatCount(bits, " {0}")} Bits!"),
            "charity_donation" =>
                (ChatMessageKind.Bits, $"{author} doou{FormatCount(metadata.GetValueOrDefault("msg-param-amount", string.Empty), " {0}")} para a caridade!"),
            "giftpaidupgrade" or "primepaidupgrade" =>
                (ChatMessageKind.Subscription, $"{author} voltou a apoiar o canal!"),
            "communitypayforward" or "standardpayforward" =>
                (ChatMessageKind.GiftSubscription, $"{author} encaminhou uma inscrição para a comunidade!"),
            "announcement" =>
                (ChatMessageKind.Announcement, $"{author} anunciou:"),
            _ => (ChatMessageKind.Chat, string.Empty)
        };
    }

    private static string FormatCount(string value, string format)
    {
        return int.TryParse(value, out var count) && count > 0
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, format, count)
            : string.Empty;
    }

    private static DateTimeOffset ParseTimestamp(string? value)
    {
        if (long.TryParse(value, out var milliseconds) &&
            milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() &&
            milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }

        return DateTimeOffset.UtcNow;
    }

    private static List<TwitchEmoteRange> ParseEmoteRanges(string rawValue)
    {
        var ranges = new List<TwitchEmoteRange>();
        foreach (var emote in rawValue.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = emote.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var id = emote[..separator];
            foreach (var rangeText in emote[(separator + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var bounds = rangeText.Split('-', 2);
                if (bounds.Length == 2 &&
                    int.TryParse(bounds[0], out var start) &&
                    int.TryParse(bounds[1], out var end) &&
                    start >= 0 && end >= start)
                {
                    ranges.Add(new TwitchEmoteRange(id, start, end));
                }
            }
        }

        return ranges.OrderBy(range => range.Start).ToList();
    }

    private static List<TwitchBadgeReference> ParseBadgeReferences(string rawValue)
    {
        var badges = new List<TwitchBadgeReference>();
        foreach (var badge in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = badge.IndexOf('/');
            if (separator <= 0 || separator == badge.Length - 1)
            {
                continue;
            }

            badges.Add(new TwitchBadgeReference(badge[..separator], badge[(separator + 1)..]));
        }

        return badges;
    }

    private static List<TwitchBadgeReference> ParseMessageBadgeReferences(
        IReadOnlyDictionary<string, string> metadata,
        bool isModerator,
        bool isVip,
        bool isSubscriber,
        bool isBroadcaster)
    {
        var badges = ParseBadgeReferences(metadata.GetValueOrDefault("badges", string.Empty));
        AddBadgeIfMissing(badges, "moderator", isModerator ? "1" : null);
        AddBadgeIfMissing(badges, "vip", isVip ? "1" : null);
        AddBadgeIfMissing(badges, "broadcaster", isBroadcaster ? "1" : null);

        if (isSubscriber)
        {
            var subscriberInfo = ParseBadgeReferences(metadata.GetValueOrDefault("badge-info", string.Empty))
                .FirstOrDefault(badge => string.Equals(badge.SetId, "subscriber", StringComparison.OrdinalIgnoreCase));
            var subscriberBadge = badges.FirstOrDefault(
                badge => string.Equals(badge.SetId, "subscriber", StringComparison.OrdinalIgnoreCase));
            AddBadgeIfMissing(badges, "subscriber", subscriberBadge?.Version ?? subscriberInfo?.Version ?? "0");
        }

        return badges;
    }

    private static void AddBadgeIfMissing(
        ICollection<TwitchBadgeReference> badges,
        string setId,
        string? version)
    {
        if (version is not null &&
            !badges.Any(badge => string.Equals(badge.SetId, setId, StringComparison.OrdinalIgnoreCase)))
        {
            badges.Add(new TwitchBadgeReference(setId, version));
        }
    }

    private static Dictionary<string, string> ParseLineTags(string rawLine)
    {
        if (!rawLine.StartsWith('@'))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var firstSpace = rawLine.IndexOf(' ');
        return firstSpace > 1
            ? ParseTagMap(rawLine[1..firstSpace])
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetPayload(string rawLine)
    {
        if (!rawLine.StartsWith('@'))
        {
            return rawLine;
        }

        var firstSpace = rawLine.IndexOf(' ');
        return firstSpace >= 0 ? rawLine[(firstSpace + 1)..] : string.Empty;
    }

    private static bool HasIrcCommand(string rawLine, string command)
    {
        var parts = GetPayload(rawLine).Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && string.Equals(parts[1], command, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ParseTagMap(string rawTags)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawTags))
        {
            return result;
        }

        foreach (var part in rawTags.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.None);
            if (pair.Length == 2)
            {
                result[pair[0]] = UnescapeTagValue(pair[1]);
            }
            else if (pair.Length == 1)
            {
                result[pair[0]] = string.Empty;
            }
        }

        return result;
    }

    private static string UnescapeTagValue(string value)
    {
        return value.Replace("\\s", " ", StringComparison.Ordinal)
            .Replace("\\:", ";", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private static string NormalizeChannel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        trimmed = trimmed.Trim().ToLowerInvariant();
        if (trimmed.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new InvalidOperationException($"O identificador '{value}' não é um nome válido de canal ou usuário Twitch.");
        }

        return trimmed;
    }

    public void Dispose()
    {
        _disposed = true;
        Disconnect();
    }
}
