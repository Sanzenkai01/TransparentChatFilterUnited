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
public sealed record TwitchChatBadge(
    string SetId,
    string Version,
    string Title,
    string ImageUrl,
    string ImageUrl2x = "",
    string ImageUrl4x = "");
public sealed record TwitchChatClear(string Channel, string? Username);

public sealed class ChatMessage
{
    private string? _rawMetadata;

    public string DisplayName { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string OriginChannel { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string SourceRoomId { get; set; } = string.Empty;
    public string ChannelImageUrl { get; set; } = string.Empty;
    public string RawMetadata
    {
        get => _rawMetadata ??= string.Join("; ", Metadata.Select(item => $"{item.Key}={item.Value}"));
        set => _rawMetadata = value;
    }
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
    public bool IsAction { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    internal IReadOnlyDictionary<string, string> Metadata { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string UsernameColor => Metadata.GetValueOrDefault("color", string.Empty);

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
    public event EventHandler<string>? ParseErrorReported;
    public event EventHandler? ReconnectRequested;

    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _connectionCts;
    private Task? _readLoopTask;
    private HashSet<string> _joinedChannels = new(StringComparer.OrdinalIgnoreCase);
    private long _connectionGeneration;
    private volatile bool _isConnected;
    private volatile bool _disposed;
    private int _parseErrorCount;
    private string _lastParseError = string.Empty;

    public bool IsConnected => _isConnected;
    public int ParseErrorCount => Volatile.Read(ref _parseErrorCount);
    public string LastParseError => Volatile.Read(ref _lastParseError);

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

                WriteLine(writer, $"PASS oauth:{normalizedToken}");
                WriteLine(writer, $"NICK {normalizedUsername}");
                WriteLine(writer, "CAP REQ :twitch.tv/tags twitch.tv/commands");
                var authentication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _readLoopTask = Task.Run(() => ReadLoopAsync(reader, writer, connectionCts, generation, authentication));
                await authentication.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);

                foreach (var joinCommand in CreateJoinCommands(channels))
                {
                    WriteLine(writer, joinCommand);
                }

                lock (_syncRoot)
                {
                    if (generation == _connectionGeneration && ReferenceEquals(_writer, writer))
                    {
                        _joinedChannels = channels.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    }
                }

                _isConnected = true;
                StatusChanged?.Invoke(this, "Conectado");
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

    public async Task UpdateChannelsAsync(IEnumerable<string> channelNames, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TwitchChatClient));
        }

        var desiredChannels = channelNames
            .Select(NormalizeChannel)
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StreamWriter? writer;
            HashSet<string> currentChannels;
            lock (_syncRoot)
            {
                if (!_isConnected || _writer is null)
                {
                    return;
                }

                writer = _writer;
                currentChannels = new HashSet<string>(_joinedChannels, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var channel in currentChannels.Except(desiredChannels, StringComparer.OrdinalIgnoreCase))
            {
                WriteLine(writer, $"PART #{channel}");
            }

            var channelsToJoin = desiredChannels.Except(currentChannels, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var joinCommand in CreateJoinCommands(channelsToJoin))
            {
                WriteLine(writer, joinCommand);
            }

            lock (_syncRoot)
            {
                if (_isConnected && ReferenceEquals(_writer, writer))
                {
                    _joinedChannels = desiredChannels;
                }
            }

            if (channelsToJoin.Length > 0 ||
                currentChannels.Except(desiredChannels, StringComparer.OrdinalIgnoreCase).Any())
            {
                StatusChanged?.Invoke(this, "Conectado");
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
            _joinedChannels.Clear();
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

                IrcLineParseResult parsed;
                try
                {
                    parsed = IrcLineParser.ParseLine(line);
                }
                catch (Exception ex) when (
                    ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
                {
                    var errorCount = Interlocked.Increment(ref _parseErrorCount);
                    Volatile.Write(ref _lastParseError, ex.GetType().Name);
                    ParseErrorReported?.Invoke(this, $"Linha IRC ignorada ({ex.GetType().Name}); total: {errorCount}.");
                    continue;
                }

                if (parsed.Command == "PING")
                {
                    var pingPayload = parsed.Trailing is not null
                        ? $":{parsed.Trailing}"
                        : string.Join(' ', parsed.Parameters);
                    WriteLine(writer, $"PONG {pingPayload}");
                }
                else if (parsed.IsWelcome)
                {
                    authentication.TrySetResult();
                }
                else if (parsed.Notice is not null)
                {
                    if (authentication.TrySetException(new InvalidOperationException(
                            $"A Twitch recusou a conexão: {parsed.Notice}. Confira usuário e token com permissão chat:read.")))
                    {
                        break;
                    }

                    StatusChanged?.Invoke(this, $"Aviso da Twitch: {parsed.Notice}");
                }
                else if (!string.IsNullOrWhiteSpace(parsed.DeletedMessageId))
                {
                    MessageDeleted?.Invoke(this, parsed.DeletedMessageId);
                }
                else if (parsed.ChatClear is not null)
                {
                    UserMessagesCleared?.Invoke(this, parsed.ChatClear);
                }
                else if (parsed.Message is not null)
                {
                    MessageReceived?.Invoke(this, parsed.Message);
                }
                else if (parsed.IsReconnect)
                {
                    authentication.TrySetException(new IOException("A Twitch solicitou uma reconexão."));
                    ReconnectRequested?.Invoke(this, EventArgs.Empty);
                    break;
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

    private static void WriteLine(StreamWriter writer, string line)
    {
        lock (writer)
        {
            writer.WriteLine(line);
        }
    }

    internal static string NormalizeChannel(string value)
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
