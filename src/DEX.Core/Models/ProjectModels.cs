namespace DEX.Core.Models;

public sealed record ScannedFile(string RelativePath, string FullPath, long Length, string Extension);

public sealed record EntryCandidate(string RelativePath, string Reason);

public sealed record DependencyItem(string Name, string? Version, string Source, bool Optional);

public sealed record LanguageHit(ProjectKind Kind, int Score, string Evidence);

public sealed class ProjectAnalysis
{
    public required string ProjectName { get; init; }
    public required string RootPath { get; init; }
    public required bool FromArchive { get; init; }
    public required ProjectKind Kind { get; init; }
    public required string LanguageName { get; init; }
    public string? Framework { get; init; }
    public required SupportLevel SupportLevel { get; init; }
    public required string StructureSummary { get; init; }
    public string? EntryPoint { get; init; }
    public IReadOnlyList<EntryCandidate> EntryCandidates { get; init; } = [];
    public bool EntryPointRequiredFromUser { get; init; }
    public IReadOnlyList<Requirement> Requirements { get; init; } = [];
    public IReadOnlyList<DependencyItem> Dependencies { get; init; } = [];
    public BuildMethod RecommendedMethod { get; init; }
    public IReadOnlyList<BuildMethod> AlternativeMethods { get; init; } = [];
    public string OutputType { get; init; } = "";
    public string? RequiredVersionText { get; init; }
    public ArchitectureKind? PreferredArchitecture { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool AnalysisTruncated { get; init; }
    public bool IsAmbiguous { get; init; }
    public IReadOnlyList<ProjectKind> AmbiguousKinds { get; init; } = [];
    public string? ProjectFile { get; init; }
    public string? TargetFramework { get; init; }
    public bool RequiresMsvc { get; init; }
    public bool HasPackageManagerWrapper { get; init; }
    public string? PackageManagerId { get; init; }
    public int PythonMagic { get; init; }
    public bool WindowedHint { get; init; }
    public IReadOnlyList<string> SecondaryLanguages { get; init; } = [];
}

public sealed class Requirement
{
    public required string ToolId { get; init; }
    public required bool IsRequired { get; init; }
    public string? RequiredVersion { get; init; }
    public required string Reason { get; init; }
}

public sealed class FormatEntry
{
    public required string Language { get; init; }
    public required string Extensions { get; init; }
    public required SupportLevel Level { get; init; }
    public required string BuildMethod { get; init; }
    public required string RequiredTools { get; init; }
    public required IReadOnlyList<string> DownloadUrls { get; init; }
    public required string Caveat { get; init; }
}
