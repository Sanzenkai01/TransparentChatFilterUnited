using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace TransparentChatFilter;

internal sealed class TwitchEmoteImageAsset
{
    private readonly BitmapSource[] _frames;
    private readonly TimeSpan[] _frameDurations;

    private TwitchEmoteImageAsset(BitmapSource[] frames, TimeSpan[] frameDurations)
    {
        _frames = frames;
        _frameDurations = frameDurations;
    }

    internal int FrameCount => _frames.Length;

    public static TwitchEmoteImageAsset Decode(byte[] imageBytes, int decodePixelWidth)
    {
        using var stream = new MemoryStream(imageBytes, writable: false);
        if (IsGif(imageBytes))
        {
            var decoder = new GifBitmapDecoder(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frames = decoder.Frames
                .Select(frame => DecodeFrame(frame, decodePixelWidth))
                .ToArray();
            var durations = decoder.Frames.Select(GetFrameDuration).ToArray();
            return new TwitchEmoteImageAsset(frames, durations);
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return new TwitchEmoteImageAsset([bitmap], [TimeSpan.FromMilliseconds(100)]);
    }

    public void ApplyTo(Image image)
    {
        image.Source = _frames[0];
        if (_frames.Length < 2 || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var animation = new ObjectAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromTicks(_frameDurations.Sum(duration => duration.Ticks))),
            RepeatBehavior = RepeatBehavior.Forever
        };
        var elapsed = TimeSpan.Zero;
        for (var index = 0; index < _frames.Length; index++)
        {
            animation.KeyFrames.Add(
                new DiscreteObjectKeyFrame(_frames[index], KeyTime.FromTimeSpan(elapsed)));
            elapsed += _frameDurations[index];
        }

        image.BeginAnimation(Image.SourceProperty, animation);
    }

    private static BitmapSource DecodeFrame(BitmapFrame frame, int decodePixelWidth)
    {
        BitmapSource decodedFrame = frame;
        if (frame.PixelWidth > decodePixelWidth)
        {
            var scale = (double)decodePixelWidth / frame.PixelWidth;
            decodedFrame = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        }

        if (decodedFrame.CanFreeze)
        {
            decodedFrame.Freeze();
        }

        return decodedFrame;
    }

    private static TimeSpan GetFrameDuration(BitmapFrame frame)
    {
        if (frame.Metadata is BitmapMetadata metadata &&
            metadata.ContainsQuery("/grctlext/Delay") &&
            metadata.GetQuery("/grctlext/Delay") is { } delayValue)
        {
            var delayInHundredthsOfASecond = Convert.ToInt32(delayValue, CultureInfo.InvariantCulture);
            return TimeSpan.FromMilliseconds(Math.Max(20, delayInHundredthsOfASecond * 10));
        }

        return TimeSpan.FromMilliseconds(100);
    }

    private static bool IsGif(byte[] imageBytes) =>
        imageBytes.Length >= 6 &&
        (imageBytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) ||
         imageBytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8));
}
