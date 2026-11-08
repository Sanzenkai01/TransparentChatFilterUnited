using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TransparentChatFilter;

internal sealed class ImagePreviewService
{
    private const int CacheCapacity = 32;
    private const int MaximumImageBytes = 5 * 1024 * 1024;
    private const int MaximumHtmlBytes = 512 * 1024;
    private const int MaximumRedirects = 3;
    private static readonly Regex UrlRegex = new(@"https://[^\s<>]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MetaTagRegex = new(@"<meta\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AttributeRegex = new(
        """(?<name>[^\s=/>]+)\s*=\s*(?:"(?<double>[^"]*)"|'(?<single>[^']*)'|(?<unquoted>[^\s>]+))""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".gif", ".jpeg", ".jpg", ".png", ".webp"
    };
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly Dispatcher _dispatcher;
    private readonly CancellationToken _lifetimeToken;
    private readonly BoundedAsyncCache<string, ImageSource> _imageCache =
        new(CacheCapacity, StringComparer.Ordinal);
    private readonly SemaphoreSlim _downloadSlots = new(3, 3);
    private int _enabled;
    private long _lastErrorReportedUtcTicks;

    public ImagePreviewService(Dispatcher dispatcher, CancellationToken lifetimeToken)
    {
        _dispatcher = dispatcher;
        _lifetimeToken = lifetimeToken;
    }

    public event EventHandler<string>? ErrorReported;

    public void SetEnabled(bool enabled) => Volatile.Write(ref _enabled, enabled ? 1 : 0);

    public async Task LoadForRowAsync(ChatRowView row, string message)
    {
        if (Volatile.Read(ref _enabled) == 0 ||
            _lifetimeToken.IsCancellationRequested ||
            FindPreviewLink(message) is not { } previewLink)
        {
            return;
        }

        try
        {
            await _downloadSlots.WaitAsync(_lifetimeToken).ConfigureAwait(false);
            ImageSource image;
            try
            {
                image = await _imageCache.GetOrCreateAsync(
                    previewLink.Uri.AbsoluteUri,
                    () => LoadPreviewAsync(previewLink.Uri, _lifetimeToken),
                    _lifetimeToken).ConfigureAwait(false);
            }
            finally
            {
                _downloadSlots.Release();
            }

            if (Volatile.Read(ref _enabled) == 0 ||
                _lifetimeToken.IsCancellationRequested)
            {
                return;
            }

            await _dispatcher.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _enabled) != 0 && !_lifetimeToken.IsCancellationRequested)
                {
                    row.SetImagePreview(image, previewLink.Start, previewLink.Length);
                }
            });
        }
        catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsPreviewFailure(exception))
        {
            ReportErrorOncePerMinute($"Não foi possível carregar uma prévia de imagem: {exception.Message}");
        }
    }

    internal static Uri? FindPreviewUri(string message)
    {
        return FindPreviewLink(message)?.Uri;
    }

    internal static (Uri Uri, int Start, int Length)? FindPreviewLink(string message)
    {
        foreach (Match match in UrlRegex.Matches(message))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', '!', '?', ')', ']', '}', '"', '\'');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                !IsSafeHttpsUri(uri))
            {
                continue;
            }

            if (IsImgurPage(uri) || ImageExtensions.Contains(Path.GetExtension(uri.AbsolutePath)))
            {
                return (uri, match.Index, candidate.Length);
            }
        }

        return null;
    }

    internal static IEnumerable<(int Start, int Length)> GetVisibleTextRanges(
        int start,
        int length,
        int hiddenStart,
        int hiddenLength)
    {
        if (length <= 0)
        {
            yield break;
        }

        var end = start + length;
        var hiddenEnd = hiddenStart + hiddenLength;
        if (hiddenLength <= 0 || hiddenEnd <= start || hiddenStart >= end)
        {
            yield return (start, length);
            yield break;
        }

        if (hiddenStart > start)
        {
            yield return (start, hiddenStart - start);
        }

        if (hiddenEnd < end)
        {
            yield return (hiddenEnd, end - hiddenEnd);
        }
    }

    internal static bool IsOnlyPreviewLinkText(string message, int linkStart, int linkLength)
    {
        if (linkStart < 0 ||
            linkLength <= 0 ||
            linkStart > message.Length ||
            linkLength > message.Length - linkStart)
        {
            return false;
        }

        var textBeforeLink = message.AsSpan(0, linkStart);
        var textAfterLink = message.AsSpan(linkStart + linkLength);
        return textBeforeLink.Trim().IsEmpty &&
               textAfterLink.Trim(" \t\r\n.,;:!?)]}".AsSpan()).IsEmpty;
    }

    internal static Uri? FindImgurImageUri(Uri pageUri, string html)
    {
        if (!IsImgurPage(pageUri))
        {
            return null;
        }

        foreach (Match tagMatch in MetaTagRegex.Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attributeMatch in AttributeRegex.Matches(tagMatch.Value))
            {
                attributes.TryAdd(
                    attributeMatch.Groups["name"].Value,
                    GetAttributeValue(attributeMatch));
            }

            if ((!attributes.TryGetValue("property", out var property) ||
                 !string.Equals(property, "og:image", StringComparison.OrdinalIgnoreCase)) &&
                (!attributes.TryGetValue("name", out var name) ||
                 !string.Equals(name, "twitter:image", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var content = attributes.GetValueOrDefault("content");
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var decodedContent = WebUtility.HtmlDecode(content);
            if (!Uri.TryCreate(pageUri, decodedContent, out var imageUri) ||
                !IsSafeHttpsUri(imageUri) ||
                !string.Equals(imageUri.Host, "i.imgur.com", StringComparison.OrdinalIgnoreCase) ||
                !ImageExtensions.Contains(Path.GetExtension(imageUri.AbsolutePath)))
            {
                continue;
            }

            return imageUri;
        }

        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = ConnectToPublicAddressAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            UseCookies = false,
            UseProxy = false
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static async ValueTask<Stream> ConnectToPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(context.DnsEndPoint.Host, out var literalAddress)
            ? [literalAddress]
            : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        foreach (var address in addresses)
        {
            if (!IsPublicAddress(address))
            {
                continue;
            }

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("O destino da prévia não resolveu para um endereço público acessível.");
    }

    private static async Task<ImageSource> LoadPreviewAsync(Uri sourceUri, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;
        var imageUri = sourceUri;

        if (IsImgurPage(sourceUri))
        {
            var html = await FetchTextAsync(sourceUri, token).ConfigureAwait(false);
            imageUri = FindImgurImageUri(sourceUri, html)
                ?? throw new InvalidDataException("A página do Imgur não contém uma imagem compatível para prévia.");
        }

        var imageBytes = await FetchImageBytesAsync(imageUri, token).ConfigureAwait(false);
        return DecodeImage(imageBytes);
    }

    private static async Task<string> FetchTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        var (response, finalUri) = await SendWithRedirectsAsync(uri, cancellationToken).ConfigureAwait(false);
        if (!IsImgurPage(finalUri))
        {
            response.Dispose();
            throw new HttpRequestException("A página do Imgur redirecionou para um endereço incompatível.");
        }

        using (response)
        {
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A página do Imgur não retornou HTML.");
            }

            var bytes = await ReadLimitedContentAsync(response, MaximumHtmlBytes, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static async Task<byte[]> FetchImageBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        var (response, finalUri) = await SendWithRedirectsAsync(uri, cancellationToken).ConfigureAwait(false);
        if (string.Equals(uri.Host, "i.imgur.com", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(finalUri.Host, "i.imgur.com", StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            throw new HttpRequestException("A imagem do Imgur redirecionou para um endereço incompatível.");
        }

        using (response)
        {
            if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
            {
                throw new InvalidDataException("O endereço não retornou um arquivo de imagem.");
            }

            return await ReadLimitedContentAsync(response, MaximumImageBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<(HttpResponseMessage Response, Uri FinalUri)> SendWithRedirectsAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var currentUri = uri;
        for (var redirectCount = 0; redirectCount <= MaximumRedirects; redirectCount++)
        {
            if (!IsSafeHttpsUri(currentUri))
            {
                throw new HttpRequestException("O redirecionamento da prévia não é seguro.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.UserAgent.ParseAdd("TransparentChatFilter/1.0");
            request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
            {
                response.EnsureSuccessStatusCode();
                return (response, currentUri);
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null ||
                redirectCount == MaximumRedirects ||
                !Uri.TryCreate(currentUri, location, out var redirectedUri))
            {
                throw new HttpRequestException("A prévia excedeu o limite de redirecionamentos.");
            }

            currentUri = redirectedUri;
        }

        throw new HttpRequestException("Não foi possível resolver o endereço da prévia.");
    }

    private static async Task<byte[]> ReadLimitedContentAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new InvalidDataException("O conteúdo da prévia excede o limite permitido.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var bytesRead = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            if (output.Length + bytesRead > maximumBytes)
            {
                throw new InvalidDataException("O conteúdo da prévia excede o limite permitido.");
            }

            output.Write(buffer, 0, bytesRead);
        }

        return output.ToArray();
    }

    private static ImageSource DecodeImage(byte[] imageBytes)
    {
        using (var validationStream = new MemoryStream(imageBytes, writable: false))
        {
            var decoder = BitmapDecoder.Create(
                validationStream,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None);
            if (decoder.Frames.Count == 0 ||
                decoder.Frames[0].PixelWidth <= 0 ||
                decoder.Frames[0].PixelHeight <= 0 ||
                (long)decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight > 16_000_000)
            {
                throw new InvalidDataException("A imagem da prévia tem dimensões inválidas ou muito grandes.");
            }
        }

        using var imageStream = new MemoryStream(imageBytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 480;
        image.StreamSource = imageStream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool IsSafeHttpsUri(Uri uri) =>
        uri.IsAbsoluteUri &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort &&
        string.IsNullOrWhiteSpace(uri.UserInfo);

    private static bool IsImgurPage(Uri uri) =>
        IsSafeHttpsUri(uri) &&
        (string.Equals(uri.Host, "imgur.com", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Host, "www.imgur.com", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 1;

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicAddress(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            return !IsInIpv4Range(value, 0x00000000, 8) &&
                   !IsInIpv4Range(value, 0x0A000000, 8) &&
                   !IsInIpv4Range(value, 0x64400000, 10) &&
                   !IsInIpv4Range(value, 0x7F000000, 8) &&
                   !IsInIpv4Range(value, 0xA9FE0000, 16) &&
                   !IsInIpv4Range(value, 0xAC100000, 12) &&
                   !IsInIpv4Range(value, 0xC0000000, 24) &&
                   !IsInIpv4Range(value, 0xC0000200, 24) &&
                   !IsInIpv4Range(value, 0xC0586300, 24) &&
                   !IsInIpv4Range(value, 0xC0A80000, 16) &&
                   !IsInIpv4Range(value, 0xC6120000, 15) &&
                   !IsInIpv4Range(value, 0xC6336400, 24) &&
                   !IsInIpv4Range(value, 0xCB007100, 24) &&
                   !IsInIpv4Range(value, 0xE0000000, 4) &&
                   !IsInIpv4Range(value, 0xF0000000, 4);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
               (bytes[0] & 0xE0) == 0x20 &&
               !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0) &&
               !(bytes[0] == 0x20 && bytes[1] == 0x02) &&
               !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8);
    }

    private static bool IsInIpv4Range(uint address, uint network, int prefixLength)
    {
        var mask = uint.MaxValue << (32 - prefixLength);
        return (address & mask) == network;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        (int)statusCode is >= 300 and < 400;

    private static string GetAttributeValue(Match match) =>
        match.Groups["double"].Success
            ? match.Groups["double"].Value
            : match.Groups["single"].Success
                ? match.Groups["single"].Value
                : match.Groups["unquoted"].Value;

    private static bool IsPreviewFailure(Exception exception) =>
        exception is HttpRequestException or IOException or InvalidDataException or
            NotSupportedException or SocketException or FormatException or ArgumentException or
            InvalidOperationException or OperationCanceledException;

    private void ReportErrorOncePerMinute(string message)
    {
        var now = DateTimeOffset.UtcNow.UtcTicks;
        while (true)
        {
            var lastReported = Volatile.Read(ref _lastErrorReportedUtcTicks);
            if (lastReported != 0 && now - lastReported < TimeSpan.FromMinutes(1).Ticks)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lastErrorReportedUtcTicks, now, lastReported) == lastReported)
            {
                ErrorReported?.Invoke(this, message);
                return;
            }
        }
    }
}
