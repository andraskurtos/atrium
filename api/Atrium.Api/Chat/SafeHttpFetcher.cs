using System.Net;
using System.Net.Sockets;

namespace Atrium.Api.Chat;

public sealed record SafeFetchResult(
    bool Success,
    string? Error,
    string? FinalUrl,
    string? ContentType,
    int StatusCode,
    byte[] BodyBytes);

public sealed class SafeHttpFetcher(
    IHttpClientFactory httpClientFactory,
    Func<string, CancellationToken, Task<IPAddress[]>>? dnsResolver = null)
{
    public const string HttpClientName = "AtriumSafeWebFetch";
    private const int MaxRedirects = 5;

    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveDns =
        dnsResolver ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));

    public async Task<SafeFetchResult> FetchAsync(
        string url,
        int maxBytes,
        bool allowPrivateNetworks = false,
        IDictionary<string, string>? headers = null,
        HttpMethod? method = null,
        HttpContent? body = null,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var currentUri)
            || (currentUri.Scheme is not ("http" or "https")))
        {
            return new SafeFetchResult(false, "Only absolute http:// and https:// URLs are allowed.", null, null, 0, []);
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        var currentMethod = method ?? HttpMethod.Get;
        HttpContent? currentBody = body;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var validationError = await ValidateUriAsync(currentUri, allowPrivateNetworks, ct);
            if (validationError is not null)
            {
                return new SafeFetchResult(false, validationError, currentUri.ToString(), null, 0, []);
            }

            using var request = new HttpRequestMessage(currentMethod, currentUri);
            request.Headers.TryAddWithoutValidation("User-Agent", "AtriumAssistant/1.0 (+https://atrium.local)");
            request.Headers.TryAddWithoutValidation("Accept-Language", "en,hu;q=0.9");
            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            if (currentBody is not null)
            {
                request.Content = currentBody;
            }

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new SafeFetchResult(false, $"Network request failed: {ex.Message}", currentUri.ToString(), null, 0, []);
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is not null)
                {
                    if (hop == MaxRedirects)
                    {
                        return new SafeFetchResult(false, "Too many redirects.", currentUri.ToString(), null, status, []);
                    }

                    currentUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);
                    if (status == 303 || (status is 301 or 302 && currentMethod == HttpMethod.Post))
                    {
                        currentMethod = HttpMethod.Get;
                        currentBody = null;
                    }

                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new SafeFetchResult(
                        false,
                        $"HTTP {status} ({response.ReasonPhrase ?? "Error"}) from {currentUri}.",
                        currentUri.ToString(),
                        response.Content.Headers.ContentType?.MediaType,
                        status,
                        []);
                }

                var bytes = await ReadCappedAsync(response.Content, maxBytes, ct);
                return new SafeFetchResult(
                    true,
                    null,
                    currentUri.ToString(),
                    response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream",
                    status,
                    bytes);
            }
        }

        return new SafeFetchResult(false, "Too many redirects.", currentUri.ToString(), null, 0, []);
    }

    private async Task<string?> ValidateUriAsync(Uri uri, bool allowPrivateNetworks, CancellationToken ct)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return "Only http:// and https:// URLs are allowed.";
        }

        if (allowPrivateNetworks) return null;

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return "URL host is missing.";
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return "Requests to localhost are blocked.";
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literalIp))
        {
            addresses = [literalIp];
        }
        else
        {
            try
            {
                addresses = await _resolveDns(uri.Host, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return $"Could not resolve host '{uri.Host}': {ex.Message}";
            }
        }

        if (addresses.Length == 0)
        {
            return $"Host '{uri.Host}' resolved to no IP addresses.";
        }

        foreach (var addr in addresses)
        {
            if (IsBlockedIpAddress(addr))
            {
                return $"Blocked request to private or local network address ({uri.Host}).";
            }
        }

        return null;
    }

    public static bool IsBlockedIpAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] == 0) return true; // 0.0.0.0/8
            if (b[0] == 10) return true; // 10.0.0.0/8
            if (b[0] == 127) return true; // 127.0.0.0/8
            if (b[0] == 169 && b[1] == 254) return true; // 169.254.0.0/16 link-local / cloud metadata
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true; // 172.16.0.0/12
            if (b[0] == 192 && b[1] == 168) return true; // 192.168.0.0/16
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true; // 100.64.0.0/10 CGNAT
            if (b[0] >= 224) return true; // multicast & reserved
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast)
            {
                return true;
            }

            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true; // fc00::/7 Unique Local Address
            return false;
        }

        return true;
    }

    private static async Task<byte[]> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        await using var ms = new MemoryStream();
        var buffer = new byte[16_384];
        var remaining = maxBytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct);
            if (read <= 0) break;
            ms.Write(buffer, 0, read);
            remaining -= read;
        }

        return ms.ToArray();
    }
}

