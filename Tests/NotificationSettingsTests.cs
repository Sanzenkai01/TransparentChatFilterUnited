using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class NotificationSettingsTests
{
    [Fact]
    public void OtherChannelNotificationCanBeDisabledWhenMessagesAreDimmed()
    {
        var settings = new AppSettings
        {
            ConnectionMode = ChatConnectionMode.AuthenticatedIrc,
            MainChannel = "main",
            DimOtherChannelMessages = true,
            NotifyOtherChannelMessages = false
        };
        var message = new ChatMessage
        {
            Channel = "main",
            OriginChannel = "joined"
        };

        Assert.False(MainWindow.ShouldPlayNotificationForMessage(message, settings));
    }

    [Fact]
    public void OtherChannelNotificationPreferenceDoesNotMuteMainChannel()
    {
        var settings = new AppSettings
        {
            ConnectionMode = ChatConnectionMode.AuthenticatedIrc,
            MainChannel = "main",
            DimOtherChannelMessages = true,
            NotifyOtherChannelMessages = false
        };
        var message = new ChatMessage
        {
            Channel = "main",
            OriginChannel = "main"
        };

        Assert.True(MainWindow.ShouldPlayNotificationForMessage(message, settings));
    }

    [Fact]
    public void OtherChannelNotificationPreferenceOnlyAppliesWhileDimmingIsEnabled()
    {
        var settings = new AppSettings
        {
            ConnectionMode = ChatConnectionMode.AuthenticatedIrc,
            MainChannel = "main",
            DimOtherChannelMessages = false,
            NotifyOtherChannelMessages = false
        };
        var message = new ChatMessage
        {
            Channel = "main",
            OriginChannel = "joined"
        };

        Assert.True(MainWindow.ShouldPlayNotificationForMessage(message, settings));
    }
}
