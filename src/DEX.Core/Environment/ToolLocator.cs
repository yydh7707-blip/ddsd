using System.Text.RegularExpressions;
using Microsoft.Win32;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Probes;

public sealed class ToolLocator
{
    public IEnumerable<string> SearchDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Split(Environment.GetEnvironmentVariable("PATH")))
            if (seen.Add(dir)) yield return dir;
        foreach (var dir in Split(ReadRegistryPath(Registry.CurrentUser, @"Environment", "Path")))
            if (seen.Add(dir)) yield return dir;
        foreach (var dir in Split(ReadRegistryPath(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", "Path")))
            if (seen.Add(dir)) yield return dir;

        foreach (var dir in KnownDirectories())
            if (seen.Add(dir)) yield return dir;
    }

    public string? Find(string fileName)
    {
        foreach (var dir in SearchDirectories())
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public IReadOnlyList<string> FindPythonInstalls()
    {
        var list = new List<string>();
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python"),
            @"C:\Python",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python")
        };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            if (File.Exists(Path.Combine(root, "python.exe")))
                list.Add(Path.Combine(root, "python.exe"));
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var exe = Path.Combine(dir, "python.exe");
                if (File.Exists(exe))
                    list.Add(exe);
            }
        }
        var onPath = Find("python.exe");
        if (onPath is not null && !list.Contains(onPath, StringComparer.OrdinalIgnoreCase))
            list.Insert(0, onPath);
        return list;
    }

    public string? FindMsBuild()
    {
        var vswhere = Find("vswhere.exe") ?? @"C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe";
        if (!File.Exists(vswhere))
            return null;
        return vswhere;
    }

    public string? FindNpmCli(string nodeExe)
    {
        var dir = Path.GetDirectoryName(nodeExe);
        if (dir is null)
            return null;
        var candidates = new[]
        {
            Path.Combine(dir, "node_modules", "npm", "bin", "npm-cli.js"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "npm", "bin", "npm-cli.js")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? ReadWebView2Version()
    {
        string[] keys =
        [
            @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",
            @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"
        ];
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var keyPath in keys)
            {
                using var key = hive.OpenSubKey(keyPath);
                var pv = key?.GetValue("pv") as string;
                if (!string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0")
                    return pv;
            }
        }
        return null;
    }

    public static bool HasTargetingPack(string version)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Reference Assemblies", "Microsoft", "Framework", ".NETFramework", "v" + version);
        return Directory.Exists(path);
    }

    public ArchitectureKind? ArchitectureOf(string exe)
    {
        try { return PeVerifier.Read(exe).Architecture; }
        catch { return null; }
    }

    private static IEnumerable<string> KnownDirectories()
    {
        yield return @"C:\Program Files\dotnet";
        yield return @"C:\Program Files\nodejs";
        yield return @"C:\Program Files\CMake\bin";
        yield return @"C:\Program Files\Go\bin";
        yield return @"C:\Program Files\Git\cmd";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin");
        yield return @"C:\msys64\mingw64\bin";
        yield return @"C:\msys64\usr\bin";
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            yield return Path.Combine(javaHome, "bin");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var adoptium = Path.Combine(programFiles, "Eclipse Adoptium");
        if (Directory.Exists(adoptium))
        {
            foreach (var dir in Directory.EnumerateDirectories(adoptium))
                yield return Path.Combine(dir, "bin");
        }
        var phpRoot = @"C:\php";
        if (Directory.Exists(phpRoot))
            yield return phpRoot;
        var ruby = @"C:\Ruby";
        if (Directory.Exists(ruby))
        {
            foreach (var dir in Directory.EnumerateDirectories(ruby))
                yield return Path.Combine(dir, "bin");
        }
    }

    private static IEnumerable<string> Split(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;
        foreach (var part in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var expanded = Environment.ExpandEnvironmentVariables(part).Trim('"');
            if (expanded.Length > 0)
                yield return expanded;
        }
    }

    private static string? ReadRegistryPath(RegistryKey hive, string subKey, string name)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }
}
