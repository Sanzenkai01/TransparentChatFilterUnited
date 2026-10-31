using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TransparentChatFilter;

internal sealed record AvailableUpdate(Version Version, Uri DownloadUri);

internal static class UpdateService
{
    private const string AssetName = "TransparentChatFilterUnited-win-x64.zip";
    private const string LatestReleaseApi =
        "https://api.github.com/repos/Sanzenkai01/TransparentChatFilterUnited/releases/latest";
    private const long MaximumArchiveSize = 500 * 1024 * 1024;
    private const string UpdateScript = """
        param([int]$ParentProcessId, [string]$ArchivePath, [string]$ExecutablePath)
        $ErrorActionPreference = 'Stop'
        $updateId = [Guid]::NewGuid().ToString('N')
        $stagedPath = "$ExecutablePath.$updateId.tmp"
        $backupPath = "$ExecutablePath.$updateId.bak"
        try {
            Wait-Process -Id $ParentProcessId -Timeout 120 -ErrorAction SilentlyContinue
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
            try {
                $entry = $archive.GetEntry('TransparentChatFilterUnited.exe')
                if ($null -eq $entry -or $entry.Length -eq 0 -or $entry.Length -gt 524288000) {
                    throw 'O pacote nao contem um executavel valido.'
                }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $stagedPath, $false)
            }
            finally {
                $archive.Dispose()
            }

            if ([IO.File]::Exists($ExecutablePath)) {
                [IO.File]::Move($ExecutablePath, $backupPath)
            }

            try {
                [IO.File]::Move($stagedPath, $ExecutablePath)
            }
            catch {
                if ([IO.File]::Exists($backupPath)) {
                    [IO.File]::Move($backupPath, $ExecutablePath)
                }
                throw
            }

            try {
                Start-Process -FilePath $ExecutablePath -ArgumentList '--show-release-notes'
            }
            catch {
                if ([IO.File]::Exists($ExecutablePath)) {
                    [IO.File]::Delete($ExecutablePath)
                }
                if ([IO.File]::Exists($backupPath)) {
                    [IO.File]::Move($backupPath, $ExecutablePath)
                }
                throw
            }

            if ([IO.File]::Exists($backupPath)) {
                try {
                    [IO.File]::Delete($backupPath)
                }
                catch {
                    Write-Warning "Nao foi possivel remover o backup: $backupPath"
                }
            }
        }
        catch {
            try {
                Add-Type -AssemblyName PresentationFramework
                [System.Windows.MessageBox]::Show(
                    "Falha ao instalar a atualizacao: $($_.Exception.Message)",
                    'Falha na atualizacao',
                    [System.Windows.MessageBoxButton]::OK,
                    [System.Windows.MessageBoxImage]::Error)
                if ([IO.File]::Exists($ExecutablePath)) {
                    Start-Process -FilePath $ExecutablePath
                }
            }
            catch {
                Write-Error "Falha ao instalar a atualizacao: $($_.Exception.Message)"
            }
        }
        finally {
            if ([IO.File]::Exists($stagedPath)) {
                [IO.File]::Delete($stagedPath)
            }
            if ([IO.File]::Exists($ArchivePath)) {
                [IO.File]::Delete($ArchivePath)
            }
            if ([IO.File]::Exists($MyInvocation.MyCommand.Path)) {
                [IO.File]::Delete($MyInvocation.MyCommand.Path)
            }
        }
        """;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal static async Task<AvailableUpdate?> CheckForUpdateAsync()
    {
        using var response = await HttpClient.GetAsync(LatestReleaseApi);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var release = await JsonDocument.ParseAsync(responseStream);
        var root = release.RootElement;
        var tag = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("A release não contém uma tag de versão.");
        var versionText = tag.StartsWith('v') ? tag[1..] : tag;

        if (!Version.TryParse(versionText, out var latestVersion))
        {
            throw new InvalidDataException($"A versão da release '{tag}' é inválida.");
        }

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version
            ?? throw new InvalidOperationException("Não foi possível determinar a versão instalada.");
        if (latestVersion <= currentVersion)
        {
            return null;
        }

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.Ordinal))
            {
                continue;
            }

            var downloadUrl = asset.GetProperty("browser_download_url").GetString()
                ?? throw new InvalidDataException("A release não contém um endereço de download.");
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri)
                || downloadUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(downloadUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("O endereço do pacote de atualização não é confiável.");
            }

            return new AvailableUpdate(latestVersion, downloadUri);
        }

        throw new InvalidDataException($"A release {tag} não contém o pacote {AssetName}.");
    }

    internal static async Task<string> DownloadUpdateAsync(AvailableUpdate update)
    {
        using var response = await HttpClient.GetAsync(
            update.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumArchiveSize)
        {
            throw new InvalidDataException("O pacote de atualização excede o tamanho permitido.");
        }

        var archivePath = Path.Combine(
            Path.GetTempPath(),
            $"TransparentChatFilterUnited-update-{Guid.NewGuid():N}.zip");

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            await source.CopyToAsync(destination);
            if (destination.Length > MaximumArchiveSize)
            {
                throw new InvalidDataException("O pacote de atualização excede o tamanho permitido.");
            }

            return archivePath;
        }
        catch
        {
            File.Delete(archivePath);
            throw;
        }
    }

    internal static void StartUpdate(string archivePath)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Não foi possível localizar o executável atual.");
        var scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"TransparentChatFilterUnited-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, UpdateScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var powershellPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo(powershellPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-STA");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add(executablePath);
        try
        {
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("Não foi possível iniciar o processo de atualização.");
            }
        }
        catch
        {
            File.Delete(scriptPath);
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TransparentChatFilterUnited/1.0");
        return client;
    }
}
