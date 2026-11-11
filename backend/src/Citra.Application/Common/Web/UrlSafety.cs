using System.Net;
using System.Net.Sockets;

namespace Citra.Application.Common.Web;

/// <summary>
/// Guards against SSRF (server-side request forgery): users give Citra URLs to fetch, and without these checks
/// someone could make the server fetch its own internal pages, the database admin, or the cloud's metadata
/// service (169.254.169.254, which hands out credentials).
/// <para>
/// <see cref="Check"/> runs when a URL is added. It cannot see where a public name points, so the fetcher must
/// also call <see cref="IsPublicAddress"/> on every address the name resolves to (and on redirects).
/// </para>
/// </summary>
public static class UrlSafety
{
    private static readonly string[] InternalHostSuffixes =
        [".localhost", ".local", ".internal", ".lan", ".home.arpa", ".intranet", ".corp"];

    /// <summary>Normalizes the URL for storing and comparing: no #fragment (same page), scheme and host lower-case.</summary>
    public static Uri Normalize(Uri url)
    {
        var builder = new UriBuilder(url) { Fragment = string.Empty };
        return builder.Uri;
    }

    /// <summary>Null if the URL may be fetched, otherwise a message explaining why not.</summary>
    public static string? Check(Uri url)
    {
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return "Only http:// and https:// links are supported.";
        }

        if (!string.IsNullOrEmpty(url.UserInfo))
        {
            return "Links with a user name or password are not supported.";
        }

        if (!url.IsDefaultPort)
        {
            return "Only links on the standard web ports (80 and 443) are supported.";
        }

        switch (url.HostNameType)
        {
            case UriHostNameType.IPv4:
            case UriHostNameType.IPv6:
                return IPAddress.TryParse(url.IdnHost.Trim('[', ']'), out var address) && IsPublicAddress(address)
                    ? null
                    : "Links to private or local network addresses are not allowed.";

            case UriHostNameType.Dns:
                var host = url.IdnHost.TrimEnd('.').ToLowerInvariant();
                if (host == "localhost" || InternalHostSuffixes.Any(host.EndsWith))
                {
                    return "Links to local or internal servers are not allowed.";
                }

                // "intranet", "db", "metadata": single-label names only exist inside private networks.
                return host.Contains('.') ? null : "Use a full public address, e.g. https://example.com/help.";

            default:
                return "This link is not a valid web address.";
        }
    }

    /// <summary>
    /// True only for addresses on the public internet. Everything local, private, link-local (cloud metadata),
    /// shared (carrier NAT), multicast or reserved is refused, for both IPv4 and IPv6.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                  // 0.0.0.0/8 "this network"
                || b[0] == 10                                   // 10.0.0.0/8 private
                || b[0] == 127                                  // 127.0.0.0/8 loopback
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // 100.64.0.0/10 carrier-grade NAT
                || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 link-local, cloud metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)      // 192.0.0.0/24 protocol assignments
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16 private
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))  // 198.18.0.0/15 benchmarking
                || b[0] >= 224);                                // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return !(IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.IPv6None)           // ::
                || address.IsIPv6LinkLocal                      // fe80::/10
                || address.IsIPv6SiteLocal                      // fec0::/10 (deprecated)
                || address.IsIPv6Multicast                      // ff00::/8
                || (b[0] & 0xFE) == 0xFC                        // fc00::/7 unique local (private)
                || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B) // 64:ff9b::/96 NAT64 → IPv4
                || (b[0] == 0x20 && b[1] == 0x02)               // 2002::/16 6to4 (embeds an IPv4)
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) // 2001::/32 Teredo (embeds an IPv4)
                || b[..12].All(x => x == 0));                   // ::/96 IPv4-compatible (::127.0.0.1), deprecated
        }

        return false;
    }
}
