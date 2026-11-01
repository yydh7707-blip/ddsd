using System.Text.RegularExpressions;

namespace DEX.Core.Security;

public static class SecurityManager
{
    public const long MaxFileBytes = 200L * 1024 * 1024;
    public const long MaxArchiveBytes = 500L * 1024 * 1024;
    public const long MaxExtractedBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxFileCount = 20_000;
    public const int MaxProbeOutputBytes = 256 * 1024;
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(45);

    private static readonly HashSet<string> AllowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "python.exe", "python3.exe", "py.exe", "pip.exe",
        "dotnet.exe", "msbuild.exe", "cmake.exe", "cpack.exe", "cl.exe", "link.exe",
        "gcc.exe", "g++.exe", "mingw32-make.exe", "make.exe", "ninja.exe",
        "go.exe", "rustc.exe", "cargo.exe", "rustup.exe",
        "java.exe", "javac.exe", "jar.exe", "jpackage.exe", "javaw.exe",
        "node.exe", "php.exe", "ruby.exe", "gem.exe",
        "vswhere.exe", "explorer.exe", "git.exe",
        "pyinstaller.exe", "nuitka.exe"
    };

    private static readonly char[] ShellMetacharacters = ['&', '|', '>', '<', '^', '%', '!', '\n', '\r', '"'];

    public static bool IsAllowedExecutable(string path)
    {
        var name = Path.GetFileName(path);
        return AllowedExecutables.Contains(name);
    }

    public static void EnsureSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Path is empty.");
        if (path.IndexOfAny(ShellMetacharacters) >= 0)
            throw new InvalidOperationException("Path contains characters that cannot be passed to a command.");
        if (path.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("Path must not contain parent segments.");
    }

    public static string SanitizeFileName(string name, string fallback = "App")
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = fallback;
        var cleaned = Regex.Replace(baseName, @"[^A-Za-z0-9._-]", "");
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.StartsWith('.'))
            cleaned = fallback;
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    public static bool IsVersionToken(string version) =>
        Regex.IsMatch(version, @"^\d+\.\d+\.\d+([\-+][A-Za-z0-9.\-]+)?$");

    public static string JobsRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "DEX", "jobs");
        Directory.CreateDirectory(root);
        return root;
    }

    public static string DataRoot()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DEX");
        Directory.CreateDirectory(root);
        return root;
    }
}
