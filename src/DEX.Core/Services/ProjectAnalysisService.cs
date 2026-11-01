using DEX.Core.Analyzers;
using DEX.Core.Compatibility;
using DEX.Core.Probes;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Services;

public sealed class ProjectAnalysisService
{
    private readonly ProjectAnalyzer _analyzer = new();
    private readonly ZipExtractor _zip = new();
    private readonly EnvironmentDetectionService _environment;
    private readonly CompatibilityEngine _compatibility = new();
    private readonly BuildConfigurationManager _configuration = new();

    public ProjectAnalysisService(EnvironmentDetectionService environment)
    {
        _environment = environment;
    }

    public async Task<(IntakeResult Intake, ProjectAnalysis Analysis, IReadOnlyList<ToolStatus> Tools, CompatibilityReport Report, OptionAvailability Options)> OpenAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var intake = Intake(path);
        var analysis = _analyzer.Analyze(intake.WorkspacePath, intake.FromArchive, intake.DisplayName);
        var ids = analysis.Requirements.Select(r => r.ToolId).Append("dotnet-sdk").Distinct();
        var versions = analysis.Requirements.Where(r => r.RequiredVersion is not null).ToDictionary(r => r.ToolId, r => r.RequiredVersion);
        var tools = await _environment.CheckRequiredAsync(ids, versions!, cancellationToken);
        var report = _compatibility.Evaluate(analysis, tools, analysis.EntryPoint);
        var options = _configuration.GetAvailability(analysis);
        return (intake, analysis, tools, report, options);
    }

    public CompatibilityReport Recheck(ProjectAnalysis analysis, IReadOnlyList<ToolStatus> tools, string? selectedEntry) =>
        _compatibility.Evaluate(analysis, tools, selectedEntry);

    public async Task<IReadOnlyList<ToolStatus>> RefreshAsync(ProjectAnalysis analysis, CancellationToken cancellationToken)
    {
        var versions = analysis.Requirements.Where(r => r.RequiredVersion is not null).ToDictionary(r => r.ToolId, r => r.RequiredVersion);
        return await _environment.CheckRequiredAsync(analysis.Requirements.Select(r => r.ToolId), versions!, cancellationToken);
    }

    public IntakeResult Intake(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
            throw new InvalidOperationException("Project path does not exist.");
        var job = Path.Combine(SecurityManager.JobsRoot(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(job);
        if (File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var extracted = _zip.Extract(path, Path.Combine(job, "src"));
            var root = SingleChildDirectory(extracted) ?? extracted;
            return new IntakeResult { WorkspacePath = root, DisplayName = Path.GetFileNameWithoutExtension(path), FromArchive = true, OriginalPath = path };
        }
        if (Directory.Exists(path))
        {
            return new IntakeResult { WorkspacePath = Path.GetFullPath(path), DisplayName = new DirectoryInfo(path).Name, FromArchive = false, OriginalPath = path };
        }
        var fileRoot = Path.Combine(job, "file");
        Directory.CreateDirectory(fileRoot);
        var info = new FileInfo(path);
        if (info.Length > SecurityManager.MaxFileBytes)
            throw new InvalidOperationException("File exceeds the size limit.");
        File.Copy(path, Path.Combine(fileRoot, info.Name));
        return new IntakeResult { WorkspacePath = fileRoot, DisplayName = Path.GetFileNameWithoutExtension(path), FromArchive = false, OriginalPath = path };
    }

    private static string? SingleChildDirectory(string root)
    {
        var dirs = Directory.GetDirectories(root);
        return dirs.Length == 1 && Directory.GetFiles(root).Length == 0 ? dirs[0] : null;
    }
}
