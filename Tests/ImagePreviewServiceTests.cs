using System.Net;
using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class ImagePreviewServiceTests
{
    [Fact]
    public void FindsImgurPageLinkWithTrailingPunctuation()
    {
        var uri = ImagePreviewService.FindPreviewUri("Olha isso: https://imgur.com/Vq6QV3w).");

        Assert.Equal("https://imgur.com/Vq6QV3w", uri?.AbsoluteUri);
    }

    [Fact]
    public void FindsExactPreviewLinkRangeWithoutTrailingPunctuation()
    {
        const string message = "antes https://imgur.com/Vq6QV3w) depois";

        var link = ImagePreviewService.FindPreviewLink(message)!.Value;

        Assert.Equal("https://imgur.com/Vq6QV3w", message.Substring(link!.Start, link.Length));
    }

    [Fact]
    public void OmitsPreviewLinkFromVisibleTextAndKeepsSurroundingText()
    {
        const string message = "antes https://imgur.com/Vq6QV3w depois";
        var link = ImagePreviewService.FindPreviewLink(message)!.Value;
        var visibleText = string.Concat(
            ImagePreviewService.GetVisibleTextRanges(0, message.Length, link.Start, link.Length)
                .Select(range => message.Substring(range.Start, range.Length)));

        Assert.Equal("antes  depois", visibleText);
    }

    [Fact]
    public void KeepsVisibleTextOutsidePreviewLinkRanges()
    {
        var ranges = ImagePreviewService.GetVisibleTextRanges(0, 20, 5, 6);

        Assert.Equal([(0, 5), (11, 9)], ranges);
    }

    [Theory]
    [InlineData("https://imgur.com/Vq6QV3w", true)]
    [InlineData("  https://imgur.com/Vq6QV3w  ", true)]
    [InlineData("https://imgur.com/Vq6QV3w).", true)]
    [InlineData("texto https://imgur.com/Vq6QV3w", false)]
    [InlineData("https://imgur.com/Vq6QV3w texto", false)]
    public void IdentifiesWhenMessageContainsOnlyPreviewLink(
        string message,
        bool expected)
    {
        var link = ImagePreviewService.FindPreviewLink(message)!.Value;

        Assert.Equal(expected, ImagePreviewService.IsOnlyPreviewLinkText(message, link.Start, link.Length));
    }

    [Fact]
    public void FindsDirectHttpsImageLink()
    {
        var uri = ImagePreviewService.FindPreviewUri("imagem https://example.com/picture.png?size=large");

        Assert.Equal("https://example.com/picture.png?size=large", uri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://example.com/picture.png")]
    [InlineData("https://example.com/page")]
    [InlineData("https://imgur.com/a/album")]
    public void IgnoresUnsupportedPreviewLinks(string message)
    {
        Assert.Null(ImagePreviewService.FindPreviewUri(message));
    }

    [Fact]
    public void ExtractsOnlyImgurCdnImageFromPageMetadata()
    {
        var page = new Uri("https://imgur.com/Vq6QV3w");
        const string html = """
            <html><head><meta content="https://i.imgur.com/Vq6QV3w.jpg" property="og:image"></head></html>
            """;

        var image = ImagePreviewService.FindImgurImageUri(page, html);

        Assert.Equal("https://i.imgur.com/Vq6QV3w.jpg", image?.AbsoluteUri);
    }

    [Fact]
    public void RejectsImgurMetadataPointingToAnotherHost()
    {
        var page = new Uri("https://imgur.com/Vq6QV3w");
        const string html = """
            <meta property="og:image" content="https://localhost/private.png">
            """;

        Assert.Null(ImagePreviewService.FindImgurImageUri(page, html));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.10.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2606:4700:4700::1111", true)]
    public void AllowsOnlyPublicAddressesForImageRequests(string address, bool expected)
    {
        Assert.Equal(expected, ImagePreviewService.IsPublicAddress(IPAddress.Parse(address)));
    }
}
