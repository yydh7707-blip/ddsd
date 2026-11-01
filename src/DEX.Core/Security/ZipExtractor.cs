using System.IO.Compression;

namespace DEX.Core.Security;

public sealed class ZipExtractor
{
    public string Extract(string zipPath, string destination)
    {
        var info = new FileInfo(zipPath);
        if (!info.Exists)
            throw new InvalidOperationException("Archive does not exist.");
        if (info.Length > SecurityManager.MaxArchiveBytes)
            throw new InvalidOperationException("Archive exceeds the size limit.");

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination);
        long total = 0;
        var count = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            count++;
            if (count > SecurityManager.MaxFileCount)
                throw new InvalidOperationException("Archive contains too many files.");

            if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')))
            {
                var dir = SafeCombine(root, entry.FullName);
                Directory.CreateDirectory(dir);
                continue;
            }

            var target = SafeCombine(root, entry.FullName);
            if (entry.Length > SecurityManager.MaxFileBytes)
                throw new InvalidOperationException($"Entry exceeds the file size limit: {entry.FullName}");
            total += entry.Length;
            if (total > SecurityManager.MaxExtractedBytes)
                throw new InvalidOperationException("Archive expands beyond the extraction limit.");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
            if (new FileInfo(target).Length != entry.Length && entry.Length > 0)
                throw new InvalidOperationException("Extracted size did not match the archive entry.");
        }

        return root;
    }

    public static string SafeCombine(string root, string entryName)
    {
        var normalized = entryName.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
            throw new InvalidOperationException("Archive entry has an absolute path.");
        if (normalized.Split('/').Any(p => p == ".."))
            throw new InvalidOperationException("Archive entry escapes the destination.");

        var combined = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var fullRoot = Path.GetFullPath(root);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            fullRoot += Path.DirectorySeparatorChar;
        if (!combined.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Archive entry escapes the destination.");
        return combined;
    }
}
