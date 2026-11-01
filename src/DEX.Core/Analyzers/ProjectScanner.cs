using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Analyzers;

public sealed class ProjectScanner
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", "__pycache__", ".venv", "venv", "dist", "target", ".vs", ".idea"
    };

    public (IReadOnlyList<ScannedFile> Files, bool Truncated, IReadOnlyList<string> Warnings) Scan(string root)
    {
        var files = new List<ScannedFile>();
        var warnings = new List<string>();
        var truncated = false;
        var fullRoot = Path.GetFullPath(root);
        foreach (var path in Enumerate(fullRoot, warnings))
        {
            if (files.Count >= SecurityManager.MaxFileCount)
            {
                truncated = true;
                warnings.Add("File scan stopped at the file-count limit.");
                break;
            }
            var info = new FileInfo(path);
            var relative = Path.GetRelativePath(fullRoot, path);
            files.Add(new ScannedFile(relative, path, info.Length, info.Extension.ToLowerInvariant()));
        }
        return (files, truncated, warnings);
    }

    private static IEnumerable<string> Enumerate(string root, List<string> warnings)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir); }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
                continue;
            }
            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (Skip.Contains(name))
                    continue;
                var attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add($"Skipped reparse point: {entry}");
                    continue;
                }
                if ((attrs & FileAttributes.Directory) != 0)
                    stack.Push(entry);
                else
                    yield return entry;
            }
        }
    }
}
