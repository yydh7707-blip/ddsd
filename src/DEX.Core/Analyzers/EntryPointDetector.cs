using System.Text.Json;
using System.Text.RegularExpressions;
using DEX.Core.Models;

namespace DEX.Core.Analyzers;

public sealed class EntryPointDetector
{
    public (string? Selected, IReadOnlyList<EntryCandidate> Candidates, bool UserMustChoose) Detect(
        string root, ProjectKind kind, IReadOnlyList<ScannedFile> files)
    {
        var candidates = new List<EntryCandidate>();
        switch (kind)
        {
            case ProjectKind.Python:
                foreach (var file in files.Where(f => f.Extension is ".py" or ".pyw"))
                {
                    var text = Read(file.FullPath);
                    if (text.Contains("__main__", StringComparison.Ordinal))
                        candidates.Add(new EntryCandidate(file.RelativePath, "if __name__ == \"__main__\""));
                }
                if (candidates.Count == 0)
                {
                    var scripts = files.Where(f => f.Extension is ".py" or ".pyw").Take(12).ToList();
                    if (scripts.Count == 1)
                        candidates.Add(new EntryCandidate(scripts[0].RelativePath, "only Python file"));
                    else
                        candidates.AddRange(scripts.Select(f => new EntryCandidate(f.RelativePath, "python file")));
                }
                break;
            case ProjectKind.DotNet:
                foreach (var file in files.Where(f => f.Extension is ".csproj" or ".vbproj" or ".fsproj"))
                {
                    var text = Read(file.FullPath);
                    var library = text.Contains("<OutputType>Library</OutputType>", StringComparison.OrdinalIgnoreCase);
                    if (!library)
                        candidates.Add(new EntryCandidate(file.RelativePath, "project output"));
                }
                break;
            case ProjectKind.Cpp:
                foreach (var file in files.Where(f => f.Extension == ".vcxproj"))
                    candidates.Add(new EntryCandidate(file.RelativePath, "vcxproj"));
                if (candidates.Count == 0 && files.Any(f => f.RelativePath.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(new EntryCandidate("CMakeLists.txt", "CMake project"));
                else if (candidates.Count == 0)
                {
                    foreach (var file in files.Where(f => f.Extension is ".c" or ".cpp" or ".cc"))
                    {
                        if (Regex.IsMatch(Read(file.FullPath), @"\bmain\s*\("))
                            candidates.Add(new EntryCandidate(file.RelativePath, "main()"));
                    }
                }
                break;
            case ProjectKind.Java:
                foreach (var file in files.Where(f => f.Extension == ".java"))
                {
                    if (Read(file.FullPath).Contains("public static void main", StringComparison.Ordinal))
                        candidates.Add(new EntryCandidate(file.RelativePath, "main method"));
                }
                var jar = files.FirstOrDefault(f => f.Extension == ".jar");
                if (jar is not null && candidates.Count == 0)
                    candidates.Add(new EntryCandidate(jar.RelativePath, "jar"));
                break;
            case ProjectKind.Go:
                foreach (var file in files.Where(f => f.Extension == ".go" && Read(f.FullPath).Contains("package main", StringComparison.Ordinal)))
                    candidates.Add(new EntryCandidate(file.RelativePath, "package main"));
                break;
            case ProjectKind.Rust:
                var mainRs = files.FirstOrDefault(f => f.RelativePath.Replace('\\', '/').EndsWith("src/main.rs", StringComparison.OrdinalIgnoreCase));
                if (mainRs is not null)
                    candidates.Add(new EntryCandidate(mainRs.RelativePath, "src/main.rs"));
                break;
            case ProjectKind.Web:
                var index = files.FirstOrDefault(f => f.RelativePath.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                    || f.RelativePath.EndsWith("index.html", StringComparison.OrdinalIgnoreCase));
                if (index is not null)
                    candidates.Add(new EntryCandidate(index.RelativePath, "index.html"));
                var packageJson = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("package.json", StringComparison.OrdinalIgnoreCase));
                if (packageJson is not null)
                    candidates.Add(new EntryCandidate(packageJson.RelativePath, "package.json"));
                break;
            case ProjectKind.Php:
                var php = files.FirstOrDefault(f => f.RelativePath.Equals("index.php", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => f.Extension == ".php");
                if (php is not null)
                    candidates.Add(new EntryCandidate(php.RelativePath, "php script"));
                break;
            case ProjectKind.Ruby:
                var rb = files.FirstOrDefault(f => f.Extension == ".rb");
                if (rb is not null)
                    candidates.Add(new EntryCandidate(rb.RelativePath, "ruby script"));
                break;
            case ProjectKind.Script:
                foreach (var file in files.Where(f => f.Extension is ".ps1" or ".bat" or ".cmd"))
                    candidates.Add(new EntryCandidate(file.RelativePath, "script"));
                break;
        }

        var distinct = candidates
            .GroupBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (distinct.Count == 1)
            return (distinct[0].RelativePath, distinct, false);
        if (distinct.Count == 0)
            return (null, distinct, kind is not ProjectKind.Resource and not ProjectKind.Unknown and not ProjectKind.Swift);
        return (null, distinct, true);
    }

    private static string Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 500_000)
                return "";
            return File.ReadAllText(path);
        }
        catch { return ""; }
    }
}
