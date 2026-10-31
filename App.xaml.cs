using System.IO;
using System.Windows;

namespace TransparentChatFilter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        var currentVersion = typeof(App).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Não foi possível determinar a versão instalada.");
        var lastSeenVersion = Version.TryParse(
            mainWindow.ViewModel.Settings.LastSeenReleaseNotesVersion,
            out var parsedVersion)
                ? parsedVersion
                : new Version(0, 0);
        if (currentVersion > lastSeenVersion)
        {
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.Loaded += (_, _) =>
            {
                var releaseNotesWindow = new ReleaseNotesWindow(currentVersion)
                {
                    Owner = mainWindow
                };
                releaseNotesWindow.ShowDialog();
                mainWindow.ViewModel.Settings.LastSeenReleaseNotesVersion = currentVersion.ToString(3);
                try
                {
                    mainWindow.ViewModel.SaveSettings();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    mainWindow.ViewModel.UpdateDiagnostic(
                        $"Não foi possível salvar a versão das novidades exibidas: {exception.Message}");
                }
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
