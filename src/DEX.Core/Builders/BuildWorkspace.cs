using DEX.Core.Probes;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class BuildWorkspace
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "__pycache__", ".venv", "venv", "bin", "obj", ".vs"
    };

    public string CreateCopy(string source)
    {
        var dest = Path.Combine(SecurityManager.JobsRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        Copy(source, dest, dest);
        return dest;
    }

    public static string Rid(ArchitectureKind kind) => kind switch
    {
        ArchitectureKind.X86 => "win-x86",
        ArchitectureKind.Arm64 => "win-arm64",
        _ => "win-x64"
    };

    public static string GoArch(ArchitectureKind kind) => kind switch
    {
        ArchitectureKind.X86 => "386",
        ArchitectureKind.Arm64 => "arm64",
        _ => "amd64"
    };

    public static string RustTarget(ArchitectureKind kind) => kind switch
    {
        ArchitectureKind.X86 => "i686-pc-windows-msvc",
        ArchitectureKind.Arm64 => "aarch64-pc-windows-msvc",
        _ => "x86_64-pc-windows-msvc"
    };

    public static async Task<ProcessResult> PublishGeneratedAsync(
        ProcessRunner runner,
        string dotnet,
        string projectDir,
        string outputDir,
        string csproj,
        string program,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectDir);
        await File.WriteAllTextAsync(Path.Combine(projectDir, "App.csproj"), csproj, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(projectDir, "Program.cs"), program, cancellationToken);
        return await runner.RunAsync(
            dotnet,
            ["publish", "App.csproj", "-c", "Release", "-o", outputDir],
            projectDir,
            null,
            TimeSpan.FromMinutes(15),
            2_000_000,
            log,
            false,
            cancellationToken);
    }

    private static void Copy(string source, string dest, string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (Skip.Contains(name))
                continue;
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            var target = Path.Combine(dest, name);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(target);
                Copy(entry, target, root);
            }
            else
            {
                var info = new FileInfo(entry);
                if (info.Length > SecurityManager.MaxFileBytes)
                    throw new InvalidOperationException("A project file exceeds the size limit.");
                Directory.CreateDirectory(dest);
                File.Copy(entry, target, overwrite: false);
            }
        }
    }
}
