using System.Text.Json;
using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class TwitchBadgeClientTests
{
    [Fact]
    public void ReadsAllTwitchBadgeScales()
    {
        using var document = JsonDocument.Parse(
            """{"data":[{"set_id":"subscriber","versions":[{"id":"12","title":"Subscriber","image_url_1x":"https://example.test/1x.png","image_url_2x":"https://example.test/2x.png","image_url_4x":"https://example.test/4x.png"}]}]}""");

        var badges = TwitchBadgeClient.ParseBadgeSets(document.RootElement);

        Assert.True(badges.TryGetValue("subscriber/12", out var badge));
        Assert.NotNull(badge);
        Assert.Equal("https://example.test/1x.png", badge.ImageUrl);
        Assert.Equal("https://example.test/2x.png", badge.ImageUrl2x);
        Assert.Equal("https://example.test/4x.png", badge.ImageUrl4x);
    }

    [Fact]
    public void FallsBackToAvailableScaleWhenHigherResolutionIsMissing()
    {
        using var document = JsonDocument.Parse(
            """{"data":[{"set_id":"vip","versions":[{"id":"1","image_url_1x":"https://example.test/1x.png","image_url_2x":"https://example.test/2x.png"}]}]}""");

        var badges = TwitchBadgeClient.ParseBadgeSets(document.RootElement);
        Assert.True(badges.TryGetValue("vip/1", out var badge));
        Assert.NotNull(badge);

        Assert.Equal(badge.ImageUrl2x, badge.ImageUrl4x);
    }
}
