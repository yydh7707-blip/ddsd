namespace DEX.Core.Security;

public static class UrlGuard
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.python.org", "python.org",
        "pyinstaller.org", "nuitka.net",
        "dotnet.microsoft.com", "learn.microsoft.com",
        "nodejs.org",
        "visualstudio.microsoft.com",
        "cmake.org",
        "www.msys2.org", "msys2.org",
        "adoptium.net",
        "go.dev",
        "www.rust-lang.org", "rust-lang.org",
        "www.php.net", "php.net", "getcomposer.org",
        "rubyinstaller.org",
        "developer.microsoft.com",
        "git-scm.com",
        "pnpm.io",
        "yarnpkg.com", "classic.yarnpkg.com",
        "maven.apache.org",
        "gradle.org",
        "www.electronjs.org", "electronjs.org",
        "wixtoolset.org"
    };

    public static bool IsAllowed(string url) => TryValidate(url, out _);

    public static void EnsureAllowed(string url)
    {
        if (!TryValidate(url, out var error))
            throw new InvalidOperationException(error);
    }

    public static bool TryValidate(string url, out string error)
    {
        error = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "URL is not absolute.";
            return false;
        }

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only HTTPS official URLs are allowed.";
            return false;
        }

        if (uri.Host.Contains("bit.ly", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Contains("tinyurl", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("aka.ms", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".aka.ms", StringComparison.OrdinalIgnoreCase))
        {
            error = "URL shorteners are not allowed.";
            return false;
        }

        if (!AllowedHosts.Contains(uri.Host))
        {
            error = "Host is not on the official allow list.";
            return false;
        }

        return true;
    }
}
