using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransparentChatFilter;

public enum ChatFilterMode
{
    OnlyMyChannel,
    JoinedChats
}

public enum OverlayTheme
{
    Dark,
    Light
}

public enum ChatConnectionMode
{
    PublicPopout,
    AuthenticatedIrc
}

public enum ChatPlatform { Twitch, YouTube }

public sealed class AppSettings
{
    public string MainChannel { get; set; } = string.Empty;
    public string YouTubeVideoId { get; set; } = string.Empty;
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
    public bool HideCommands { get; set; }
    public bool ShowTimestamps { get; set; } = true;
    public bool ShowBadges { get; set; } = true;
    public bool ShowUserNames { get; set; } = true;
    public bool ShowModerators { get; set; } = true;
    public bool ShowVips { get; set; } = true;
    public bool EnableSound { get; set; } = false;
    public bool AutoReconnect { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool HideSettingsDuringUse { get; set; } = false;
    public bool ClickThroughEnabled { get; set; } = false;
    public bool IsTransparent { get; set; } = false;
    public bool ShowSettingsPanel { get; set; } = true;
    public OverlayTheme Theme { get; set; } = OverlayTheme.Dark;
    public double BackgroundOpacity { get; set; } = 0.86;
    public double TextOpacity { get; set; } = 1.0;
    public int FontSize { get; set; } = 14;
    public int MessageSpacing { get; set; } = 6;
    public int MaxVisibleMessages { get; set; } = 80;
    public string AccentColor { get; set; } = "#5ea2ff";
    public string[] RecentChannels { get; set; } = Array.Empty<string>();

    [JsonIgnore]
    public bool YouTubeOAuthClientIdMigrated { get; private set; }

    [JsonIgnore]
    public string SettingsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TransparentChatFilter",
        "settings.json");

    public static AppSettings Load(string? path = null)
    {
        var targetPath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TransparentChatFilter",
            "settings.json");

        if (!File.Exists(targetPath))
        {
            return new AppSettings { SettingsPath = targetPath };
        }

        try
        {
            var json = File.ReadAllText(targetPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.TryGetProperty("YouTubeOAuthClientId", out var clientId) &&
                    clientId.ValueKind == JsonValueKind.String &&
                    !string.Equals(clientId.GetString(), YouTubeOAuthClient.ClientId, StringComparison.Ordinal))
                {
                    settings.YouTubeOAuthClientIdMigrated = true;
                }

                if (string.IsNullOrWhiteSpace(settings.YouTubeVideoId) &&
                    document.RootElement.TryGetProperty("YouTubeLiveChatId", out var previousVideoId) &&
                    previousVideoId.ValueKind == JsonValueKind.String)
                {
                    settings.YouTubeVideoId = previousVideoId.GetString() ?? string.Empty;
                }
            }

            settings.SettingsPath = targetPath;
            if (!Enum.IsDefined(settings.FilterMode))
            {
                settings.FilterMode = ChatFilterMode.JoinedChats;
            }

            if (!Enum.IsDefined(settings.ConnectionMode))
            {
                settings.ConnectionMode = ChatConnectionMode.PublicPopout;
            }

            if (Math.Abs(settings.BackgroundOpacity - 0.55) < 0.001)
            {
                settings.BackgroundOpacity = 0.86;
            }

            return settings;
        }
        catch
        {
            return new AppSettings { SettingsPath = targetPath };
        }
    }

    public void Save()
    {
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
