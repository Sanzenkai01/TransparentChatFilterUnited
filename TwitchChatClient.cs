using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TransparentChatFilter;

public sealed class ChatMessage
{
    public ChatPlatform Platform { get; set; } = ChatPlatform.Twitch;
    public string DisplayName { get; set; } = string.Empty;
    public string LoginName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string OriginChannel { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string SourceRoomId { get; set; } = string.Empty;
    public string RawMetadata { get; set; } = string.Empty;
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

                if (line.Contains(" 001 ", StringComparison.Ordinal))
                {
                    authentication.TrySetResult();
                    continue;
                }

                if (line.Contains("NOTICE", StringComparison.OrdinalIgnoreCase))
                {
                    var notice = line.Contains(" :")
                        ? line[(line.LastIndexOf(" :", StringComparison.Ordinal) + 2)..]
                        : "A Twitch recusou a autenticação.";
                    authentication.TrySetException(new InvalidOperationException(
                        $"A Twitch recusou a conexão: {notice}. Confira usuário e token com permissão chat:read."));
                    break;
                }

                if (line.Contains("PRIVMSG", StringComparison.OrdinalIgnoreCase))
                {
                    var message = ParsePrivMessage(line);
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
            var tagsText = string.Empty;
            var payload = rawLine;

            if (rawLine.StartsWith("@", StringComparison.Ordinal))
            {
                var firstSpace = rawLine.IndexOf(' ');
                if (firstSpace > 0)
                {
                    tagsText = rawLine[..firstSpace].TrimStart('@');
                    payload = rawLine[(firstSpace + 1)..];
                }
            }

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

            var metadata = ParseTagMap(tagsText);
            var displayName = metadata.TryGetValue("display-name", out var name) ? name : "Unknown";
            var channelName = NormalizeChannel(channelToken);
            var roomId = metadata.GetValueOrDefault("room-id", string.Empty);
            var sourceRoomId = metadata.GetValueOrDefault("source-room-id", string.Empty);
            var originChannel = channelName;
            var color = metadata.TryGetValue("color", out var rawColor) ? rawColor : string.Empty;
            var isVip = metadata.TryGetValue("vip", out var vipValue) && string.Equals(vipValue, "1", StringComparison.Ordinal);
            var isMod = metadata.TryGetValue("mod", out var modValue) && string.Equals(modValue, "1", StringComparison.Ordinal);
            var isSubscriber = metadata.TryGetValue("subscriber", out var subValue) && string.Equals(subValue, "1", StringComparison.Ordinal);

            return new ChatMessage
            {
                DisplayName = displayName,
                LoginName = prefix.StartsWith(':') && prefix.Contains('!')
                    ? prefix[1..prefix.IndexOf('!')]
                    : string.Empty,
                Message = messageText,
                Channel = channelName,
                OriginChannel = originChannel,
                RoomId = roomId,
                SourceRoomId = sourceRoomId,
                RawMetadata = string.Join("; ", metadata.Select(item => $"{item.Key}={item.Value}")),
                IsVip = isVip,
                IsModerator = isMod,
                IsSubscriber = isSubscriber,
                IsBroadcaster = string.Equals(metadata.GetValueOrDefault("room-id"), metadata.GetValueOrDefault("user-id"), StringComparison.Ordinal),
                TimestampUtc = DateTimeOffset.UtcNow
            };
        }
        catch
        {
            return null;
        }
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
                result[pair[0]] = pair[1];
            }
            else if (pair.Length == 1)
            {
                result[pair[0]] = string.Empty;
            }
        }

        return result;
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
