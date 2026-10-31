using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransparentChatFilter;

public enum ChatFilterMode
{
    OnlyMyChannel,
    JoinedChats
}

public enum ChatConnectionMode
{
    PublicPopout,
    AuthenticatedIrc
}

public enum MessageNotificationSound
{
    Neutral,
    Soft,
    Bell
}

public sealed class AppSettings
{
    private const int CurrentSettingsVersion = 2;

    public string MainChannel { get; set; } = string.Empty;
    public string TwitchUsername { get; set; } = string.Empty;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public ChatConnectionMode ConnectionMode { get; set; } = ChatConnectionMode.PublicPopout;
    public ChatFilterMode FilterMode { get; set; } = ChatFilterMode.OnlyMyChannel;
    public List<string> SelectedChannels { get; set; } = new();
    public List<string> BlockedChannels { get; set; } = new();
    public List<string> BlockedUsers { get; set; } = new();
    public string BlockedTerms { get; set; } = string.Empty;
    public string HighlightTerms { get; set; } = string.Empty;
    public bool HideCommands { get; set; }
    public bool ShowTimestamps { get; set; } = true;
    public bool ShowBadges { get; set; } = true;
    public bool ShowUserNames { get; set; } = true;
    public bool UseTwitchNameColors { get; set; } = true;
    public bool ShowModerators { get; set; } = true;
    public bool ShowVips { get; set; } = true;
    public bool DimOtherChannelMessages { get; set; }
    public bool EnableSound { get; set; } = false;
    public bool NotifyNewMessages { get; set; } = true;
    public int SettingsVersion { get; set; }
    public string LastSeenReleaseNotesVersion { get; set; } = string.Empty;
    public MessageNotificationSound NotificationSound { get; set; } = MessageNotificationSound.Neutral;
    public int NotificationVolumePercent { get; set; } = 50;
    public bool AutoReconnect { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool HideSettingsDuringUse { get; set; } = false;
    public bool ClickThroughEnabled { get; set; } = false;
    public bool IsTransparent { get; set; } = false;
    public bool ShowSettingsPanel { get; set; } = true;
    public double BackgroundOpacity { get; set; } = 0.86;
    public double TextOpacity { get; set; } = 1.0;
    public bool UseMessageBackgroundOpacity { get; set; } = true;
    public double MessageBackgroundOpacity { get; set; } = 0.75;
    public int FontSize { get; set; } = 14;
    public int MessageSpacing { get; set; } = 6;
    public int MaxVisibleMessages { get; set; } = 80;
    public string AccentColor { get; set; } = "#5ea2ff";
    public string MessageBackgroundColor { get; set; } = "#161D2C";
    public string[] RecentChannels { get; set; } = Array.Empty<string>();

    [JsonIgnore]
    public string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TransparentChatFilter",
        "settings.json");

    internal static string PendingReleaseNotesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TransparentChatFilter",
        "pending-release-notes.txt");

    public static AppSettings Load(string? path = null)
    {
        var targetPath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TransparentChatFilter",
            "settings.json");

        if (!File.Exists(targetPath))
        {
            return new AppSettings
            {
                SettingsPath = targetPath,
                SettingsVersion = CurrentSettingsVersion
            };
        }

        try
        {
            var json = File.ReadAllText(targetPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            settings.SettingsPath = targetPath;
            if (settings.SettingsVersion < CurrentSettingsVersion)
            {
                settings.NotifyNewMessages = true;
                settings.UseMessageBackgroundOpacity = true;
                settings.SettingsVersion = CurrentSettingsVersion;
            }

            if (!Enum.IsDefined(settings.FilterMode))
            {
                settings.FilterMode = ChatFilterMode.JoinedChats;
            }

            if (!Enum.IsDefined(settings.ConnectionMode))
            {
                settings.ConnectionMode = ChatConnectionMode.PublicPopout;
            }

            if (!Enum.IsDefined(settings.NotificationSound))
            {
                settings.NotificationSound = MessageNotificationSound.Neutral;
            }

            settings.NotificationVolumePercent = Math.Clamp(settings.NotificationVolumePercent, 0, 100);
            settings.TextOpacity = Math.Clamp(settings.TextOpacity, 0.2, 1.0);
            settings.MessageBackgroundOpacity = Math.Clamp(settings.MessageBackgroundOpacity, 0.1, 1.0);
            settings.MessageSpacing = Math.Clamp(settings.MessageSpacing, 0, 20);
            settings.BlockedTerms ??= string.Empty;
            settings.HighlightTerms ??= string.Empty;

            if (Math.Abs(settings.BackgroundOpacity - 0.55) < 0.001)
            {
                settings.BackgroundOpacity = 0.86;
            }

            return settings;
        }
        catch
        {
            return new AppSettings
            {
                SettingsPath = targetPath,
                SettingsVersion = CurrentSettingsVersion
            };
        }
    }

    public void Save()
    {
        SettingsVersion = CurrentSettingsVersion;
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(SettingsPath, json);
    }
}
