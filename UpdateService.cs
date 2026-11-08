using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TransparentChatFilter;

internal sealed record AvailableUpdate(
    Version Version,
    Uri DownloadUri,
    Uri ChecksumUri,
    string ReleaseNotes,
    string Changelog);

internal sealed record UpdateCheckResult(
    AvailableUpdate? Update,
    string? ETag,
    bool NotModified = false,
    bool RateLimited = false);

internal static class UpdateService
{
    private const string AssetName = "TransparentChatFilterUnited-win-x64.zip";
    private const string ChecksumAssetName = AssetName + ".sha256";
    private const string LatestReleaseApi =
        "https://api.github.com/repos/Sanzenkai01/TransparentChatFilterUnited/releases/latest";
    private const long MaximumArchiveSize = 500 * 1024 * 1024;
    private const string UpdateScript = """
        param(
            [int]$ParentProcessId,
            [string]$ArchivePath,
            [string]$ExecutablePath,
            [string]$ReleaseNotesMarkerPath,
            [string]$ReleaseNotesContentPath,
            [string]$UpdatedVersion)
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
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReleaseNotesMarkerPath)) | Out-Null
                [IO.File]::WriteAllText($ReleaseNotesMarkerPath, $UpdatedVersion, [Text.Encoding]::ASCII)
                Start-Process -FilePath $ExecutablePath
            }
            catch {
                try {
                    [IO.File]::Delete($ReleaseNotesMarkerPath)
                    [IO.File]::Delete($ReleaseNotesContentPath)
                }
                catch {
                    Write-Warning "Nao foi possivel remover o marcador de novidades."
                }
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
                [IO.File]::Delete($ReleaseNotesContentPath)
            }
            catch {
                Write-Warning "Nao foi possivel remover o conteudo das novidades."
            }
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
    private static readonly HttpClient ApiHttpClient = CreateHttpClient(TimeSpan.FromSeconds(20));
    private static readonly HttpClient DownloadHttpClient = CreateHttpClient(Timeout.InfiniteTimeSpan);

    internal static async Task<UpdateCheckResult> CheckForUpdateAsync(
        string? etag,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        if (!string.IsNullOrWhiteSpace(etag) &&
            EntityTagHeaderValue.TryParse(etag, out var entityTag))
        {
            request.Headers.IfNoneMatch.Add(entityTag);
        }

        using var response = await ApiHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new UpdateCheckResult(null, response.Headers.ETag?.ToString() ?? etag, NotModified: true);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new UpdateCheckResult(null, response.Headers.ETag?.ToString() ?? etag, RateLimited: true);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var rateLimitHeader = response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
                && remainingValues.Any(value => value == "0");
            if (rateLimitHeader ||
                response.Headers.RetryAfter is not null ||
                errorBody.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult(null, response.Headers.ETag?.ToString() ?? etag, RateLimited: true);
            }
        }

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var release = await JsonDocument.ParseAsync(
            responseStream,
            cancellationToken: cancellationToken);
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
            return new UpdateCheckResult(null, response.Headers.ETag?.ToString() ?? etag);
        }

        Uri? downloadUri = null;
        Uri? checksumUri = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var assetName = asset.GetProperty("name").GetString();
            if (!string.Equals(assetName, AssetName, StringComparison.Ordinal) &&
                !string.Equals(assetName, ChecksumAssetName, StringComparison.Ordinal))
            {
                continue;
            }

            var assetUrl = asset.GetProperty("browser_download_url").GetString()
                ?? throw new InvalidDataException($"A release não contém um endereço para {assetName}.");
            if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var validatedUri)
                || validatedUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(validatedUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("O endereço do pacote de atualização não é confiável.");
            }

            if (string.Equals(assetName, AssetName, StringComparison.Ordinal))
            {
                downloadUri = validatedUri;
            }
            else
            {
                checksumUri = validatedUri;
            }
        }

        if (downloadUri is null)
        {
            throw new InvalidDataException($"A release {tag} não contém o pacote {AssetName}.");
        }

        if (checksumUri is null)
        {
            throw new InvalidDataException($"A release {tag} não contém a soma de verificação {ChecksumAssetName}.");
        }

        var releaseNotes = root.TryGetProperty("body", out var bodyElement)
            ? bodyElement.GetString() ?? string.Empty
            : string.Empty;
        var changelog = await FetchChangelogAsync(
            currentVersion,
            latestVersion,
            releaseNotes,
            cancellationToken);
        return new UpdateCheckResult(
            new AvailableUpdate(latestVersion, downloadUri, checksumUri, releaseNotes, changelog),
            response.Headers.ETag?.ToString() ?? etag);
    }

    private static async Task<string> FetchChangelogAsync(
        Version currentVersion,
        Version latestVersion,
        string releaseNotes,
        CancellationToken cancellationToken)
    {
        var commitMessages = new List<string>();
        var totalCommitCount = 0;
        try
        {
            var comparisonUrl =
                $"https://api.github.com/repos/Sanzenkai01/TransparentChatFilterUnited/compare/" +
                $"v{currentVersion.ToString(3)}...v{latestVersion.ToString(3)}";
            for (var page = 1; page <= 3; page++)
            {
                var separator = comparisonUrl.Contains('?') ? '&' : '?';
                using var response = await ApiHttpClient.GetAsync(
                    $"{comparisonUrl}{separator}per_page=100&page={page}",
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var comparison = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                totalCommitCount = ReleaseChangelog.ReadTotalCommitCount(comparison.RootElement);
                var pageMessages = ReleaseChangelog.ReadCommitMessages(comparison.RootElement);
                commitMessages.AddRange(pageMessages);
                if (pageMessages.Count < 100 || commitMessages.Count >= totalCommitCount)
                {
                    break;
                }
            }

            var returnedCount = Math.Min(commitMessages.Count, ReleaseChangelog.MaximumCommits);
            return ReleaseChangelog.Format(
                currentVersion.ToString(3),
                latestVersion.ToString(3),
                releaseNotes,
                commitMessages.Take(returnedCount).ToArray(),
                totalCommitCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            return BuildUnavailableChangelog(currentVersion, latestVersion, releaseNotes, commitMessages, totalCommitCount, exception);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or JsonException or InvalidOperationException or
                InvalidDataException)
        {
            return BuildUnavailableChangelog(
                currentVersion,
                latestVersion,
                releaseNotes,
                commitMessages,
                totalCommitCount,
                exception);
        }
    }

    private static string BuildUnavailableChangelog(
        Version currentVersion,
        Version latestVersion,
        string releaseNotes,
        IReadOnlyList<string> commitMessages,
        int totalCommitCount,
        Exception exception)
    {
        var fallback = ReleaseChangelog.Format(
            currentVersion.ToString(3),
            latestVersion.ToString(3),
            releaseNotes,
            commitMessages.Take(ReleaseChangelog.MaximumCommits).ToArray(),
            totalCommitCount);
        return $"Não foi possível carregar a lista completa de alterações do GitHub: {exception.Message}" +
               $"{Environment.NewLine}{Environment.NewLine}{fallback}";
    }

    internal static async Task<string> DownloadUpdateAsync(
        AvailableUpdate update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var expectedHash = await DownloadExpectedHashAsync(update.ChecksumUri, cancellationToken);

        var archivePath = Path.Combine(
            Path.GetTempPath(),
            $"TransparentChatFilterUnited-update-{Guid.NewGuid():N}.zip");

        try
        {
            using var response = await DownloadHttpClient.GetAsync(
                update.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaximumArchiveSize)
            {
                throw new InvalidDataException("O pacote de atualização excede o tamanho permitido.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long totalBytes = 0;
            var contentLength = response.Content.Headers.ContentLength;
            progress?.Report(0);

            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytes += bytesRead;
                if (totalBytes > MaximumArchiveSize)
                {
                    throw new InvalidDataException("O pacote de atualização excede o tamanho permitido.");
                }

                hash.AppendData(buffer, 0, bytesRead);
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                if (contentLength is > 0)
                {
                    progress?.Report(Math.Clamp((double)totalBytes / contentLength.Value, 0, 1));
                }
            }

            if (totalBytes == 0)
            {
                throw new InvalidDataException("O pacote de atualização está vazio.");
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(expectedHash)))
            {
                throw new InvalidDataException(
                    "A verificação SHA-256 do pacote falhou. O arquivo baixado não corresponde ao publicado; nenhuma atualização foi aplicada.");
            }

            progress?.Report(1);
            return archivePath;
        }
        catch
        {
            File.Delete(archivePath);
            throw;
        }
    }

    internal static void StartUpdate(string archivePath, AvailableUpdate update)
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
        startInfo.ArgumentList.Add(AppSettings.PendingReleaseNotesPath);
        startInfo.ArgumentList.Add(AppSettings.PendingReleaseNotesContentPath);
        startInfo.ArgumentList.Add(update.Version.ToString(3));
        try
        {
            AtomicFile.WriteAllText(AppSettings.PendingReleaseNotesContentPath, update.Changelog);
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("Não foi possível iniciar o processo de atualização.");
            }
        }
        catch
        {
            File.Delete(scriptPath);
            if (File.Exists(AppSettings.PendingReleaseNotesContentPath))
            {
                File.Delete(AppSettings.PendingReleaseNotesContentPath);
            }

            throw;
        }
    }

    private static async Task<string> DownloadExpectedHashAsync(Uri checksumUri, CancellationToken cancellationToken)
    {
        using var response = await DownloadHttpClient.GetAsync(
            checksumUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 1024)
        {
            throw new InvalidDataException("O arquivo de soma de verificação excede o tamanho permitido.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[256];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (memory.Length + bytesRead > 1024)
            {
                throw new InvalidDataException("O arquivo de soma de verificação excede o tamanho permitido.");
            }

            memory.Write(buffer, 0, bytesRead);
        }

        var checksumText = Encoding.ASCII.GetString(memory.ToArray()).Trim();
        var hash = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("A release publicou um arquivo SHA-256 inválido.");
        }

        return hash;
    }

    private static HttpClient CreateHttpClient(TimeSpan timeout)
    {
        var client = new HttpClient
        {
            Timeout = timeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TransparentChatFilterUnited/1.0");
        return client;
    }
}
