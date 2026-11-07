using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class MessageHistoryTests
{
    [Fact]
    public void TrimmingHistoryKeepsOnlyNewestMessagesWithinConfiguredLimit()
    {
        var history = Enumerable.Range(1, 15)
            .Select(index => new ChatMessage { Message = index.ToString() })
            .ToList();

        MainWindow.TrimMessageHistory(history, 10);

        Assert.Equal(10, history.Count);
        Assert.Equal("6", history[0].Message);
        Assert.Equal("15", history[^1].Message);
    }
}
