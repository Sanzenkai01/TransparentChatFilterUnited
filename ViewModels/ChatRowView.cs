using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TransparentChatFilter;

public sealed class ChatRowView : INotifyPropertyChanged
{
    private Brush _nameBrush = Brushes.White;
    private string _timestampText = string.Empty;
    private double _maxWidth = double.PositiveInfinity;

    public static ImageSource TwitchImage { get; } = new BitmapImage(new Uri("pack://application:,,,/twitch.png"));
    public ImageSource PlatformImage { get; set; } = TwitchImage;
    public string ChannelImageUrl { get; set; } = string.Empty;
    public Brush MessageBackgroundBrush { get; set; } = Brushes.Transparent;
    public Brush EventAccentBrush { get; set; } = Brushes.Transparent;
    public IReadOnlyList<TwitchChatBadge> Badges { get; set; } = Array.Empty<TwitchChatBadge>();
    public string PlatformName { get; set; } = "Twitch";
    public ChatMessage SourceMessage { get; set; } = new();
    public IReadOnlyList<TwitchEmoteRange> Emotes { get; set; } = Array.Empty<TwitchEmoteRange>();
    public bool IsSystemEvent { get; set; }
    public bool IsNewMessage { get; set; }
    public bool IsAuthenticatedIrc { get; set; }
    public bool IsTransparent { get; set; }
    public bool UserNameAboveMessage { get; set; }
    public bool ShowChatTextOutline { get; set; }
    public int FontSize { get; set; }
    public double LineHeight { get; set; }
    public double TextOpacity { get; set; } = 1;
    public double MaxWidth
    {
        get => _maxWidth;
        set
        {
            if (_maxWidth.Equals(value))
            {
                return;
            }

            _maxWidth = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaxWidth)));
        }
    }
    public string SystemText { get; set; } = string.Empty;
    public Thickness RowMargin { get; set; }
    public double RowOpacity { get; set; } = 1;
    public string FullTimestampText { get; set; } = string.Empty;
    public string TimestampText
    {
        get => _timestampText;
        private set
        {
            if (_timestampText == value)
            {
                return;
            }

            _timestampText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimestampText)));
        }
    }
    public string DisplayName { get; set; } = string.Empty;
    public string BadgeText { get; set; } = string.Empty;
    public string NameSeparator { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public Brush DefaultNameBrush { get; set; } = Brushes.White;
    public Brush NameBrush
    {
        get => _nameBrush;
        private set
        {
            if (ReferenceEquals(_nameBrush, value))
            {
                return;
            }

            _nameBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameBrush)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetTransparentAppearance(bool enabled)
    {
        TimestampText = enabled ? string.Empty : FullTimestampText;
        NameBrush = DefaultNameBrush;
    }
}
