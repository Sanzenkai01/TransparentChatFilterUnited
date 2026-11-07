using System.Globalization;
using System.Text;

namespace TransparentChatFilter;

internal sealed record IrcLineParseResult(
    string Command,
    IReadOnlyDictionary<string, string> Tags,
    string Prefix,
    IReadOnlyList<string> Parameters,
    string? Trailing,
    ChatMessage? Message = null,
    string? DeletedMessageId = null,
    TwitchChatClear? ChatClear = null,
    string? Notice = null,
    bool IsWelcome = false,
    bool IsReconnect = false);

internal static class IrcLineParser
{
    public static IrcLineParseResult ParseLine(string line)
    {
        var index = 0;
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (index < line.Length && line[index] == '@')
        {
            var tagsEnd = line.IndexOf(' ', index);
            if (tagsEnd < 0)
            {
                throw new FormatException("A linha IRC contém tags sem comando.");
            }

            ParseTags(line.AsSpan(index + 1, tagsEnd - index - 1), tags);
            index = SkipSpaces(line, tagsEnd);
        }

        var prefix = string.Empty;
        if (index < line.Length && line[index] == ':')
        {
            var prefixEnd = line.IndexOf(' ', index);
            if (prefixEnd < 0)
            {
                throw new FormatException("A linha IRC contém um prefixo sem comando.");
            }

            prefix = line[(index + 1)..prefixEnd];
            index = SkipSpaces(line, prefixEnd);
        }

        var commandStart = index;
        while (index < line.Length && line[index] != ' ')
        {
            index++;
        }

        if (commandStart == index)
        {
            throw new FormatException("A linha IRC não contém um comando.");
        }

        var command = line[commandStart..index].ToUpperInvariant();
        var parameters = new List<string>(4);
        string? trailing = null;
        while ((index = SkipSpaces(line, index)) < line.Length)
        {
            if (line[index] == ':')
            {
                trailing = line[(index + 1)..];
                break;
            }

            var parameterStart = index;
            while (index < line.Length && line[index] != ' ')
            {
                index++;
            }

            parameters.Add(line[parameterStart..index]);
            if (parameters.Count > 14)
            {
                throw new FormatException("A linha IRC excedeu o limite de parâmetros.");
            }
        }

        var parsedLine = new IrcLineParseResult(command, tags, prefix, parameters, trailing);
        return command switch
        {
            "PING" or "PONG" or "001" or "NOTICE" or "CLEARMSG" or "CLEARCHAT" or
                "PRIVMSG" or "USERNOTICE" or "RECONNECT" => Dispatch(parsedLine),
            _ => parsedLine
        };
    }

    private static IrcLineParseResult Dispatch(IrcLineParseResult line)
    {
        switch (line.Command)
        {
            case "PING":
            case "PONG":
                return line;
            case "001":
                return line with { IsWelcome = true };
            case "NOTICE":
                return line with { Notice = line.Trailing ?? "A Twitch recusou a autenticação." };
            case "RECONNECT":
                return line with { IsReconnect = true };
            case "CLEARMSG":
                return line with { DeletedMessageId = line.Tags.GetValueOrDefault("target-msg-id") };
            case "CLEARCHAT":
                if (line.Parameters.Count == 0)
                {
                    throw new FormatException("CLEARCHAT não contém o canal.");
                }

                var channel = TwitchChatClient.NormalizeChannel(line.Parameters[0]);
                var username = line.Trailing?.Trim();
                return line with
                {
                    ChatClear = string.IsNullOrWhiteSpace(channel)
                        ? null
                        : new TwitchChatClear(channel, string.IsNullOrWhiteSpace(username) ? null : username)
                };
            case "PRIVMSG":
                return line with { Message = ParsePrivMessage(line) };
            case "USERNOTICE":
                return line with { Message = ParseUserNotice(line) };
            default:
                return line;
        }
    }

    private static ChatMessage ParsePrivMessage(IrcLineParseResult line)
    {
        if (line.Parameters.Count == 0 || line.Trailing is null)
        {
            throw new FormatException("PRIVMSG não contém canal ou mensagem.");
        }

        var metadata = line.Tags;
        var channel = TwitchChatClient.NormalizeChannel(line.Parameters[0]);
        var displayName = metadata.GetValueOrDefault("display-name", "Unknown");
        var isVip = metadata.GetValueOrDefault("vip") == "1";
        var isModerator = metadata.GetValueOrDefault("mod") == "1";
        var isSubscriber = metadata.GetValueOrDefault("subscriber") == "1";
        var isBroadcaster = string.Equals(
            metadata.GetValueOrDefault("room-id"),
            metadata.GetValueOrDefault("user-id"),
            StringComparison.Ordinal);
        var bits = metadata.GetValueOrDefault("bits", string.Empty);
        var isCheer = int.TryParse(bits, out var bitCount) && bitCount > 0;
        var messageText = line.Trailing;
        var isAction = messageText.StartsWith("\u0001ACTION ", StringComparison.Ordinal) &&
                       messageText.EndsWith('\u0001');
        if (isAction)
        {
            messageText = messageText[8..^1];
        }

        var author = string.IsNullOrWhiteSpace(displayName) ? "Alguém" : displayName;
        return new ChatMessage
        {
            DisplayName = displayName,
            LoginName = GetLoginName(line.Prefix),
            Message = messageText,
            IsAction = isAction,
            Channel = channel,
            OriginChannel = channel,
            RoomId = metadata.GetValueOrDefault("room-id", string.Empty),
            SourceRoomId = metadata.GetValueOrDefault("source-room-id", string.Empty),
            Metadata = metadata,
            MessageId = metadata.GetValueOrDefault("id", string.Empty),
            Emotes = ParseEmoteRanges(metadata.GetValueOrDefault("emotes", string.Empty), messageText),
            BadgeReferences = ParseMessageBadgeReferences(metadata, isModerator, isVip, isSubscriber, isBroadcaster),
            Kind = isCheer ? ChatMessageKind.Bits : ChatMessageKind.Chat,
            SystemText = isCheer ? $"{author} enviou {bitCount} Bits!" : string.Empty,
            IsVip = isVip,
            IsModerator = isModerator,
            IsSubscriber = isSubscriber,
            IsBroadcaster = isBroadcaster,
            TimestampUtc = ParseTimestamp(metadata.GetValueOrDefault("tmi-sent-ts"))
        };
    }

    private static ChatMessage? ParseUserNotice(IrcLineParseResult line)
    {
        if (line.Parameters.Count == 0)
        {
            throw new FormatException("USERNOTICE não contém canal.");
        }

        var metadata = line.Tags;
        var channel = TwitchChatClient.NormalizeChannel(line.Parameters[0]);
        var displayName = metadata.GetValueOrDefault("display-name", string.Empty);
        var eventId = metadata.GetValueOrDefault("msg-id", string.Empty);
        var (kind, systemText) = CreateNoticeText(eventId, displayName, metadata);
        if (string.IsNullOrWhiteSpace(systemText))
        {
            return null;
        }

        return new ChatMessage
        {
            DisplayName = displayName,
            LoginName = metadata.GetValueOrDefault("login", string.Empty),
            Message = line.Trailing ?? string.Empty,
            SystemText = systemText,
            Kind = kind,
            Channel = channel,
            OriginChannel = channel,
            RoomId = metadata.GetValueOrDefault("room-id", string.Empty),
            SourceRoomId = metadata.GetValueOrDefault("source-room-id", string.Empty),
            Metadata = metadata,
            MessageId = metadata.GetValueOrDefault("id", string.Empty),
            Emotes = ParseEmoteRanges(metadata.GetValueOrDefault("emotes", string.Empty), line.Trailing ?? string.Empty),
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

    private static string GetLoginName(string prefix)
    {
        var separator = prefix.IndexOf('!');
        return separator > 0 ? prefix[..separator] : string.Empty;
    }

    private static void ParseTags(ReadOnlySpan<char> rawTags, IDictionary<string, string> tags)
    {
        while (!rawTags.IsEmpty)
        {
            var separator = rawTags.IndexOf(';');
            var part = separator < 0 ? rawTags : rawTags[..separator];
            var equals = part.IndexOf('=');
            var key = equals < 0 ? part : part[..equals];
            if (!key.IsEmpty)
            {
                tags[key.ToString()] = equals < 0 ? string.Empty : UnescapeTagValue(part[(equals + 1)..]);
            }

            rawTags = separator < 0 ? ReadOnlySpan<char>.Empty : rawTags[(separator + 1)..];
        }
    }

    private static string UnescapeTagValue(ReadOnlySpan<char> value)
    {
        if (value.IndexOf('\\') < 0)
        {
            return value.ToString();
        }

        var decoded = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\' || index + 1 >= value.Length)
            {
                if (character != '\\')
                {
                    decoded.Append(character);
                }

                continue;
            }

            character = value[++index] switch
            {
                ':' => ';',
                's' => ' ',
                '\\' => '\\',
                'r' => '\r',
                'n' => '\n',
                var escaped => escaped
            };
            decoded.Append(character);
        }

        return decoded.ToString();
    }

    private static int SkipSpaces(string text, int index)
    {
        while (index < text.Length && text[index] == ' ')
        {
            index++;
        }

        return index;
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
            "sub" => (ChatMessageKind.Subscription, $"{author} se inscreveu no canal!"),
            "resub" => (ChatMessageKind.Resubscription, $"{author} renovou a inscrição{FormatCount(months, " por {0} meses")}!"),
            "subgift" or "anonsubgift" => (ChatMessageKind.GiftSubscription, $"{(eventId == "anonsubgift" ? "Alguém" : author)} presenteou {recipient} com uma inscrição!"),
            "submysterygift" or "anonsubmysterygift" => (ChatMessageKind.GiftSubscription, $"{author} presenteou a comunidade{FormatCount(gifts, " com {0} inscrições")}!"),
            "raid" => (ChatMessageKind.Raid, $"{author} chegou em raid{FormatCount(viewers, " com {0} espectadores")}!"),
            "bitsbadger" or "bitscharity" => (ChatMessageKind.Bits, $"{author} enviou{FormatCount(bits, " {0}")} Bits!"),
            "charity_donation" => (ChatMessageKind.Bits, $"{author} doou{FormatCount(metadata.GetValueOrDefault("msg-param-amount", string.Empty), " {0}")} para a caridade!"),
            "giftpaidupgrade" or "primepaidupgrade" => (ChatMessageKind.Subscription, $"{author} voltou a apoiar o canal!"),
            "communitypayforward" or "standardpayforward" => (ChatMessageKind.GiftSubscription, $"{author} encaminhou uma inscrição para a comunidade!"),
            "announcement" => (ChatMessageKind.Announcement, $"{author} anunciou:"),
            _ => (ChatMessageKind.Chat, string.Empty)
        };
    }

    private static string FormatCount(string value, string format) =>
        int.TryParse(value, out var count) && count > 0
            ? string.Format(CultureInfo.InvariantCulture, format, count)
            : string.Empty;

    private static DateTimeOffset ParseTimestamp(string? value) =>
        long.TryParse(value, out var milliseconds) &&
        milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() &&
        milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;

    private static List<TwitchEmoteRange> ParseEmoteRanges(string rawValue, string messageText)
    {
        var ranges = new List<TwitchEmoteRange>();
        foreach (var emote in rawValue.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = emote.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            foreach (var rangeText in emote[(separator + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var bounds = rangeText.Split('-', 2);
                if (bounds.Length == 2 &&
                    int.TryParse(bounds[0], out var start) &&
                    int.TryParse(bounds[1], out var end) &&
                    start >= 0 && end >= start)
                {
                    var utf16Start = GetUtf16Offset(messageText, start);
                    var utf16End = GetUtf16Offset(messageText, end);
                    if (utf16Start >= 0 && utf16End >= utf16Start)
                    {
                        ranges.Add(new TwitchEmoteRange(emote[..separator], utf16Start, utf16End));
                    }
                }
            }
        }

        return ranges.OrderBy(range => range.Start).ToList();
    }

    private static int GetUtf16Offset(string text, int codePointIndex)
    {
        var currentCodePoint = 0;
        var utf16Offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (currentCodePoint == codePointIndex)
            {
                return utf16Offset;
            }

            utf16Offset += rune.Utf16SequenceLength;
            currentCodePoint++;
        }

        return currentCodePoint == codePointIndex ? utf16Offset : -1;
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

    private static List<TwitchBadgeReference> ParseBadgeReferences(string rawValue)
    {
        var badges = new List<TwitchBadgeReference>();
        foreach (var badge in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = badge.IndexOf('/');
            if (separator > 0 && separator < badge.Length - 1)
            {
                badges.Add(new TwitchBadgeReference(badge[..separator], badge[(separator + 1)..]));
            }
        }

        return badges;
    }

    private static void AddBadgeIfMissing(ICollection<TwitchBadgeReference> badges, string setId, string? version)
    {
        if (version is not null &&
            !badges.Any(badge => string.Equals(badge.SetId, setId, StringComparison.OrdinalIgnoreCase)))
        {
            badges.Add(new TwitchBadgeReference(setId, version));
        }
    }
}
