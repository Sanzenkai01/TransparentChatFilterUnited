using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class TwitchEmoteImageAssetTests
{
    [Fact]
    public void DecodePreservesAnimationFramesFromGif()
    {
        RunOnSta(() =>
        {
            var asset = TwitchEmoteImageAsset.Decode(CreateAnimatedGif(), 16);
            var image = new Image();
            asset.ApplyTo(image);

            Assert.Equal(2, asset.FrameCount);
            Assert.NotNull(image.Source);
            Assert.Equal(SystemParameters.ClientAreaAnimation, image.HasAnimatedProperties);
        });
    }

    private static byte[] CreateAnimatedGif()
    {
        var encoder = new GifBitmapEncoder();
        encoder.Frames.Add(CreateGifFrame(Colors.Red));
        encoder.Frames.Add(CreateGifFrame(Colors.Blue));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapFrame CreateGifFrame(Color color)
    {
        var pixels = new[] { color.B, color.G, color.R, color.A };
        var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        bitmap.Freeze();
        var metadata = new BitmapMetadata("gif");
        metadata.SetQuery("/grctlext/Delay", (ushort)8);
        return BitmapFrame.Create(bitmap, null, metadata, null);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }
}
