using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class IrcLineParserTests
{
    [Fact]
    public void ParsesPrivMessageWithEmotesBadgesAndColor()
    {
        var color = $"#{18:X2}{52:X2}{86:X2}";
        var parsed = IrcLineParser.ParseLine(
            $"@badge-info=subscriber/12;badges=moderator/1,subscriber/12;display-name=TestUser;emotes=25:0-4;mod=1;subscriber=1;user-id=7;room-id=9;color={color} :testuser!testuser@testuser.tmi.twitch.tv PRIVMSG #canal :Kappa hello");

        var message = Assert.IsType<ChatMessage>(parsed.Message);
        Assert.Equal("TestUser", message.DisplayName);
        Assert.Equal("Kappa hello", message.Message);
        Assert.Equal("canal", message.Channel);
        Assert.Equal(color, message.UsernameColor);
        Assert.True(message.IsModerator);
        Assert.True(message.IsSubscriber);
        Assert.Contains(message.BadgeReferences, badge => badge.SetId == "moderator" && badge.Version == "1");
        Assert.Contains(message.BadgeReferences, badge => badge.SetId == "subscriber" && badge.Version == "12");
        Assert.Contains(message.Emotes, emote => emote.Id == "25" && emote.Start == 0 && emote.End == 4);
    }

    [Fact]
    public void ConvertsCodePointEmoteOffsetsToUtf16WhenMessageStartsWithEmoji()
    {
        var parsed = IrcLineParser.ParseLine(
            "@display-name=TestUser;emotes=25:2-6 :testuser!testuser@testuser.tmi.twitch.tv PRIVMSG #canal :😀 Kappa");

        var emote = Assert.Single(Assert.IsType<ChatMessage>(parsed.Message).Emotes);
        Assert.Equal(3, emote.Start);
        Assert.Equal(7, emote.End);
        Assert.Equal("Kappa", parsed.Message!.Message.Substring(emote.Start, emote.End - emote.Start + 1));
    }

    [Fact]
    public void InterpretsActionEmoteOffsetsAfterCtcpPrefix()
    {
        var parsed = IrcLineParser.ParseLine(
            "@display-name=TestUser;emotes=25:0-4 :testuser!testuser@testuser.tmi.twitch.tv PRIVMSG #canal :\u0001ACTION Kappa\u0001");

        var message = Assert.IsType<ChatMessage>(parsed.Message);
        Assert.True(message.IsAction);
        var emote = Assert.Single(message.Emotes);
        Assert.Equal("Kappa", message.Message.Substring(emote.Start, emote.End - emote.Start + 1));
    }

    [Fact]
    public void ParsesActionWithoutCtcpEnvelope()
    {
        var parsed = IrcLineParser.ParseLine(
            "@display-name=TestUser :testuser!testuser@testuser.tmi.twitch.tv PRIVMSG #canal :\u0001ACTION waves\u0001");

        var message = Assert.IsType<ChatMessage>(parsed.Message);
        Assert.True(message.IsAction);
        Assert.Equal("waves", message.Message);
    }

    [Theory]
    [InlineData("sub", ChatMessageKind.Subscription, "TestUser se inscreveu no canal!")]
    [InlineData("resub", ChatMessageKind.Resubscription, "TestUser renovou a inscrição por 12 meses!")]
    [InlineData("raid", ChatMessageKind.Raid, "TestUser chegou em raid com 34 espectadores!")]
    public void ParsesUserNoticeEvents(string eventId, ChatMessageKind expectedKind, string expectedText)
    {
        var parameters = eventId switch
        {
            "resub" => "msg-param-months=12",
            "raid" => "msg-param-viewerCount=34",
            _ => string.Empty
        };
        var parsed = IrcLineParser.ParseLine(
            $"@display-name=TestUser;login=testuser;msg-id={eventId};{parameters} :tmi.twitch.tv USERNOTICE #canal");

        var message = Assert.IsType<ChatMessage>(parsed.Message);
        Assert.Equal(expectedKind, message.Kind);
        Assert.Equal(expectedText, message.SystemText);
    }

    [Fact]
    public void ParsesClearMessageTagAndCommand()
    {
        var parsed = IrcLineParser.ParseLine(
            "@target-msg-id=message-123;login=testuser :tmi.twitch.tv CLEARMSG #canal :mensagem removida");

        Assert.Equal("CLEARMSG", parsed.Command);
        Assert.Equal("message-123", parsed.DeletedMessageId);
        Assert.Equal("mensagem removida", parsed.Trailing);
    }

    [Fact]
    public void ParsesClearChatTarget()
    {
        var parsed = IrcLineParser.ParseLine(
            "@ban-duration=60 :tmi.twitch.tv CLEARCHAT #canal :testuser");

        Assert.Equal(new TwitchChatClear("canal", "testuser"), parsed.ChatClear);
    }

    [Fact]
    public void UnescapesIrcv3TagsInOnePass()
    {
        var parsed = IrcLineParser.ParseLine(
            @"@display-name=First\sLast;note=semi\:colon;slash=backslash\\;newline=line\nbreak;carriage=line\rreturn;unknown=x\q :user!user@host PRIVMSG #canal :hello");

        Assert.Equal("First Last", parsed.Tags["display-name"]);
        Assert.Equal("semi;colon", parsed.Tags["note"]);
        Assert.Equal(@"backslash\", parsed.Tags["slash"]);
        Assert.Equal("line\nbreak", parsed.Tags["newline"]);
        Assert.Equal("line\rreturn", parsed.Tags["carriage"]);
        Assert.Equal("xq", parsed.Tags["unknown"]);
    }

    [Theory]
    [InlineData("@display-name=Broken PRIVMSG")]
    [InlineData(":prefix-without-command")]
    [InlineData(" ")]
    public void RejectsMalformedLinesAsLineScopedParseErrors(string line)
    {
        Assert.Throws<FormatException>(() => IrcLineParser.ParseLine(line));
    }

    [Fact]
    public void InvalidUserNoticeChannelIsRejectedByParser()
    {
        Assert.Throws<InvalidOperationException>(() => IrcLineParser.ParseLine(
            "@display-name=Bad;msg-id=sub :tmi.twitch.tv USERNOTICE #invalid-name"));
    }

    [Fact]
    public void ParsesTwitchReconnectCommand()
    {
        var parsed = IrcLineParser.ParseLine(":tmi.twitch.tv RECONNECT");

        Assert.True(parsed.IsReconnect);
    }
}
