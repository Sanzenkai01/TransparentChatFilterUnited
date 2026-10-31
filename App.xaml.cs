using System.Windows;

namespace TransparentChatFilter;

public partial class App : Application
{
    private const string ReleaseNotesArgument = "--show-release-notes";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        var showReleaseNotes = Array.Exists(
            e.Args,
            argument => string.Equals(argument, ReleaseNotesArgument, StringComparison.OrdinalIgnoreCase));
        if (showReleaseNotes)
        {
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Loaded += (_, _) =>
            {
                var version = typeof(App).Assembly.GetName().Version ?? new Version(1, 1, 7);
                var releaseNotesWindow = new ReleaseNotesWindow(version)
                {
                    Owner = mainWindow
                };
                releaseNotesWindow.ShowDialog();
            };
        }

        mainWindow.Show();
        _ = CheckForUpdatesAsync();
    }

    private static async Task CheckForUpdatesAsync()
    {
        UpdateCheckWindow? progressWindow = null;

        try
        {
            progressWindow = new UpdateCheckWindow("Checando atualizações...")
            {
                Owner = Current.MainWindow
            };
            progressWindow.Show();

            var updateTask = UpdateService.CheckForUpdateAsync();
            await Task.WhenAll(updateTask, Task.Delay(TimeSpan.FromMilliseconds(1500)));
            var update = await updateTask;
            progressWindow.Close();
            progressWindow = null;

            if (update is null)
            {
                return;
            }

            var updatePrompt = new UpdateAvailableWindow(update.Version)
            {
                Owner = Current.MainWindow
            };
            if (updatePrompt.ShowDialog() != true)
            {
                return;
            }

            progressWindow = new UpdateCheckWindow("Baixando atualização...")
            {
                Owner = Current.MainWindow
            };
            progressWindow.Show();

            var archivePath = await UpdateService.DownloadUpdateAsync(update);
            UpdateService.StartUpdate(archivePath);
            Current.Shutdown();
        }
        catch (Exception exception)
        {
            progressWindow?.Close();
            progressWindow = null;
            MessageBox.Show(
                $"Não foi possível verificar ou baixar atualizações: {exception.Message}",
                "Atualização",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            progressWindow?.Close();
        }
    }
}
