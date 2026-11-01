using System.Text.Json;
using System.Xml.Linq;
using DEX.Core.Models;

namespace DEX.Core.Analyzers;

public sealed class FrameworkDetector
{
    public (string? Framework, string? PackageManager, bool HasWrapper, string? TargetFramework, bool RequiresMsvc, bool Windowed) Detect(
        string root, ProjectKind kind, IReadOnlyList<ScannedFile> files)
    {
        string? framework = null;
        string? packageManager = null;
        var hasWrapper = false;
        string? tfm = null;
        var requiresMsvc = files.Any(f => f.Extension == ".vcxproj");
        var windowed = files.Any(f => f.Extension == ".pyw");

        if (kind == ProjectKind.Web)
        {
            var pkg = files.FirstOrDefault(f => f.RelativePath.Equals("package.json", StringComparison.OrdinalIgnoreCase)
                || f.RelativePath.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));
            if (pkg is not null)
                framework = ReadWebFramework(pkg.FullPath, out packageManager);
            if (files.Any(f => f.RelativePath.EndsWith("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase)))
                packageManager = "pnpm";
            else if (files.Any(f => f.RelativePath.EndsWith("yarn.lock", StringComparison.OrdinalIgnoreCase)))
                packageManager = "yarn";
            else if (pkg is not null && packageManager is null)
                packageManager = "npm";
            if (framework is null && files.Any(f => f.Extension is ".html" or ".htm"))
                framework = "Static HTML";
        }

        if (kind == ProjectKind.DotNet)
        {
            var project = files.FirstOrDefault(f => f.Extension is ".csproj" or ".vbproj" or ".fsproj");
            if (project is not null)
            {
                var text = SafeRead(project.FullPath);
                tfm = Between(text, "<TargetFramework>", "</TargetFramework>")
                    ?? Between(text, "<TargetFrameworkVersion>", "</TargetFrameworkVersion>");
                if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
                    framework = "ASP.NET Core";
                else if (text.Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase))
                    framework = "WPF";
                else if (text.Contains("Microsoft.NET.Sdk.WindowsDesktop", StringComparison.OrdinalIgnoreCase) || text.Contains("<UseWindowsForms>true</UseWindowsForms>", StringComparison.OrdinalIgnoreCase))
                    framework = "Windows Desktop";
                else if (tfm?.StartsWith("v4", StringComparison.OrdinalIgnoreCase) == true || tfm?.StartsWith("net4", StringComparison.OrdinalIgnoreCase) == true)
                    framework = ".NET Framework";
                else
                    framework = ".NET";
                windowed = text.Contains("WinExe", StringComparison.OrdinalIgnoreCase) || framework is "WPF" or "Windows Desktop";
            }
        }

        if (kind == ProjectKind.Java)
        {
            hasWrapper = files.Any(f => f.RelativePath.EndsWith("mvnw.cmd", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith("gradlew.bat", StringComparison.OrdinalIgnoreCase));
            if (files.Any(f => f.RelativePath.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase)))
                packageManager = "maven";
            else if (files.Any(f => f.RelativePath.EndsWith("build.gradle", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith("build.gradle.kts", StringComparison.OrdinalIgnoreCase)))
                packageManager = "gradle";
            framework = packageManager ?? "Java";
        }

        if (kind == ProjectKind.Cpp && files.Any(f => f.RelativePath.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)))
        {
            var cmake = SafeRead(files.First(f => f.RelativePath.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)).FullPath);
            if (cmake.Contains("MSVC", StringComparison.OrdinalIgnoreCase))
                requiresMsvc = true;
            framework = "CMake";
        }
        else if (kind == ProjectKind.Cpp && requiresMsvc)
            framework = "MSVC";

        if (kind == ProjectKind.Python)
            framework = files.Any(f => f.RelativePath.EndsWith("pyproject.toml", StringComparison.OrdinalIgnoreCase)) ? "pyproject" : "Python";
        if (kind == ProjectKind.Go)
            framework = "Go modules";
        if (kind == ProjectKind.Rust)
            framework = "Cargo";
        if (kind == ProjectKind.Php)
            framework = "PHP";
        if (kind == ProjectKind.Ruby)
            framework = "Ruby";

        return (framework, packageManager, hasWrapper, tfm, requiresMsvc, windowed);
    }

    private static string? ReadWebFramework(string path, out string? packageManager)
    {
        packageManager = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.TryGetProperty("packageManager", out var pm))
            {
                var value = pm.GetString() ?? "";
                if (value.StartsWith("pnpm", StringComparison.OrdinalIgnoreCase)) packageManager = "pnpm";
                else if (value.StartsWith("yarn", StringComparison.OrdinalIgnoreCase)) packageManager = "yarn";
                else if (value.StartsWith("npm", StringComparison.OrdinalIgnoreCase)) packageManager = "npm";
            }
            var names = new List<string>();
            Collect(root, "dependencies", names);
            Collect(root, "devDependencies", names);
            if (names.Any(n => n.Equals("electron", StringComparison.OrdinalIgnoreCase))) return "Electron";
            if (names.Any(n => n.StartsWith("react", StringComparison.OrdinalIgnoreCase))) return "React";
            if (names.Any(n => n.StartsWith("vue", StringComparison.OrdinalIgnoreCase))) return "Vue";
            if (names.Any(n => n.Contains("svelte", StringComparison.OrdinalIgnoreCase))) return "Svelte";
            if (names.Any(n => n.Equals("next", StringComparison.OrdinalIgnoreCase))) return "Next.js";
            if (names.Any(n => n.Equals("typescript", StringComparison.OrdinalIgnoreCase))) return "TypeScript";
            return "Node.js";
        }
        catch
        {
            return null;
        }
    }

    private static void Collect(JsonElement root, string property, List<string> names)
    {
        if (!root.TryGetProperty(property, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return;
        foreach (var prop in obj.EnumerateObject())
            names.Add(prop.Name);
    }

    private static string SafeRead(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 1_000_000)
                return "";
            return File.ReadAllText(path);
        }
        catch { return ""; }
    }

    private static string? Between(string text, string start, string end)
    {
        var i = text.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        i += start.Length;
        var j = text.IndexOf(end, i, StringComparison.OrdinalIgnoreCase);
        return j < 0 ? null : text[i..j].Trim();
    }
}
