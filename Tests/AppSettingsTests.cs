using System.Text.Json;
using TransparentChatFilter;
using Xunit;

namespace TransparentChatFilter.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void MissingFileReturnsDefaultsWithoutWarning()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");

        var settings = AppSettings.Load(path);

        Assert.Equal(path, settings.SettingsPath);
        Assert.Equal(0.86, settings.BackgroundOpacity);
        Assert.Equal(20, settings.MaxVisibleMessages);
        Assert.True(settings.NotifyOtherChannelMessages);
        Assert.False(settings.ShowChatTextOutline);
        Assert.False(settings.ShowImagePreviews);
        Assert.False(settings.StartTransparentAndConnected);
        Assert.Null(settings.LoadWarning);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SaveCreatesFileAndKeepsPreviousContentsAsBackup()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var settings = AppSettings.Load(path);
        settings.MainChannel = "first";
        settings.Save();
        settings.MainChannel = "second";
        settings.Save();

        Assert.Equal("second", AppSettings.Load(path).MainChannel);
        Assert.Equal("first", JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path + ".bak"))!.MainChannel);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    public void InvalidFileIsPreservedAndDefaultsAreReturned(string contents)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, contents);

        var settings = AppSettings.Load(path);

        Assert.Equal(0.86, settings.BackgroundOpacity);
        Assert.Contains("inválidas", settings.LoadWarning, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(directory.Path, "settings.corrupt-*.json"));
    }

    [Fact]
    public void InvalidPrimaryFileFallsBackToBackupAndIsPreserved()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{truncated");
        File.WriteAllText(path + ".bak", """{"MainChannel":"recovered","SettingsVersion":3}""");

        var settings = AppSettings.Load(path);

        Assert.Equal("recovered", settings.MainChannel);
        Assert.Contains("recuperadas", settings.LoadWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(directory.Path, "settings.corrupt-*.json"));
    }

    [Fact]
    public void BackgroundOpacityLegacyValueMigratesOnlyForOlderSettingsVersion()
    {
        using var directory = new TemporaryDirectory();
        var oldPath = Path.Combine(directory.Path, "old.json");
        var currentPath = Path.Combine(directory.Path, "current.json");
        File.WriteAllText(oldPath, """{"BackgroundOpacity":0.55,"SettingsVersion":2}""");
        File.WriteAllText(currentPath, """{"BackgroundOpacity":0.55,"SettingsVersion":3}""");

        Assert.Equal(0.86, AppSettings.Load(oldPath).BackgroundOpacity);
        Assert.Equal(0.55, AppSettings.Load(currentPath).BackgroundOpacity);
    }

    [Fact]
    public void LegacyAccentMigratesToHighlightColorAndOldDefaultsMigrate()
    {
        using var directory = new TemporaryDirectory();
        var oldPath = Path.Combine(directory.Path, "old.json");
        File.WriteAllText(oldPath,
            """{"SettingsVersion":3,"AccentColor":"#A970FF","MessageBackgroundColor":"#161D2C","ShowChatTextOutline":true}""");

        var migrated = AppSettings.Load(oldPath);

        Assert.Equal(RgbHex(169, 112, 255), migrated.HighlightColor);
        Assert.Equal(RgbHex(39, 43, 47), migrated.MessageBackgroundColor);
        Assert.False(migrated.ShowChatTextOutline);
    }

    [Fact]
    public void CurrentSettingsPreserveExplicitOutlineAndTransparentStartupPreference()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path,
            """{"SettingsVersion":8,"ShowChatTextOutline":true,"StartTransparentAndConnected":true,"UserNameAboveMessage":true}""");

        var settings = AppSettings.Load(path);

        Assert.True(settings.ShowChatTextOutline);
        Assert.True(settings.StartTransparentAndConnected);
        Assert.True(settings.UserNameAboveMessage);
        Assert.Equal(100, settings.ImagePreviewOpacityPercent);
    }

    [Fact]
    public void MissingOtherChannelSoundPreferenceDefaultsToEnabled()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, """{"SettingsVersion":9}""");

        Assert.True(AppSettings.Load(path).NotifyOtherChannelMessages);
    }

    [Fact]
    public void ImagePreviewPreferenceDefaultsDisabledAndPersists()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var settings = AppSettings.Load(path);

        Assert.False(settings.ShowImagePreviews);
        Assert.Equal(100, settings.ImagePreviewOpacityPercent);

        settings.ShowImagePreviews = true;
        settings.ImagePreviewOpacityPercent = 72;
        settings.Save();

        var reloaded = AppSettings.Load(path);
        Assert.True(reloaded.ShowImagePreviews);
        Assert.Equal(72, reloaded.ImagePreviewOpacityPercent);
    }

    [Theory]
    [InlineData(-25, 0)]
    [InlineData(45, 45)]
    [InlineData(125, 100)]
    public void LoadedImagePreviewOpacityIsClampedAndPreserved(int configuredOpacity, int expectedOpacity)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(
            path,
            $$"""{"SettingsVersion":9,"ImagePreviewOpacityPercent":{{configuredOpacity}}}""");

        Assert.Equal(expectedOpacity, AppSettings.Load(path).ImagePreviewOpacityPercent);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(500, 300)]
    public void LoadedMaximumVisibleMessagesIsClamped(int configuredMaximum, int expectedMaximum)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, $$"""{"SettingsVersion":8,"MaxVisibleMessages":{{configuredMaximum}}}""");

        Assert.Equal(expectedMaximum, AppSettings.Load(path).MaxVisibleMessages);
    }

    [Fact]
    public void PreviousDefaultMessageLimitMigratesToTwentyButCustomLimitIsPreserved()
    {
        using var directory = new TemporaryDirectory();
        var oldDefaultPath = Path.Combine(directory.Path, "old-default.json");
        var customPath = Path.Combine(directory.Path, "custom.json");
        File.WriteAllText(oldDefaultPath, """{"SettingsVersion":8,"MaxVisibleMessages":80}""");
        File.WriteAllText(customPath, """{"SettingsVersion":8,"MaxVisibleMessages":120}""");

        Assert.Equal(20, AppSettings.Load(oldDefaultPath).MaxVisibleMessages);
        Assert.Equal(120, AppSettings.Load(customPath).MaxVisibleMessages);
    }

    [Fact]
    public void PreviouslySavedDefaultOutlineIsDisabledDuringVersionEightMigration()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path,
            """{"SettingsVersion":7,"ShowChatTextOutline":true}""");

        Assert.False(AppSettings.Load(path).ShowChatTextOutline);
    }

    private static string RgbHex(byte red, byte green, byte blue) =>
        $"#{red:X2}{green:X2}{blue:X2}";

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "TransparentChatFilterTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
