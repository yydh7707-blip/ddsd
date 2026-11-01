namespace DEX.Core.Models;

public sealed class ToolStatus
{
    public required string ToolId { get; init; }
    public required string Name { get; init; }
    public required InstallState State { get; init; }
    public string? Version { get; init; }
    public string? ExecutablePath { get; init; }
    public string? Detail { get; init; }
    public bool FoundOutsidePath { get; init; }
    public ArchitectureKind? Architecture { get; init; }
    public IReadOnlyList<string> ExtraVersions { get; init; } = [];
}

public sealed class CompatibilityReport
{
    public required CompatibilityStatus Status { get; init; }
    public required bool BuildAllowed { get; init; }
    public required string Summary { get; init; }
    public required string Problem { get; init; }
    public required string Needed { get; init; }
    public required string Resolution { get; init; }
    public IReadOnlyList<RequirementResult> Results { get; init; } = [];
    public IReadOnlyList<string> OptionalNotes { get; init; } = [];
}

public sealed class RequirementResult
{
    public required Requirement Requirement { get; init; }
    public required ToolStatus Status { get; init; }
    public required bool Satisfied { get; init; }
}

public sealed class BuildOptions
{
    public string OutputName { get; set; } = "App";
    public string OutputDirectory { get; set; } = "";
    public string? IconPath { get; set; }
    public string ProductName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string ProductVersion { get; set; } = "1.0.0";
    public ArchitectureKind Architecture { get; set; } = ArchitectureKind.X64;
    public bool Console { get; set; } = true;
    public bool SingleFile { get; set; } = true;
    public bool SelfContained { get; set; } = true;
    public bool IncludeDependencies { get; set; } = true;
    public bool Optimize { get; set; } = true;
    public bool Compression { get; set; }
    public BuildMethod Method { get; set; }
    public string? SelectedEntry { get; set; }
}

public sealed class OptionAvailability
{
    public bool OutputName { get; init; } = true;
    public bool Icon { get; init; }
    public bool ProductName { get; init; }
    public bool CompanyName { get; init; }
    public bool ProductVersion { get; init; }
    public bool Console { get; init; }
    public bool SingleFile { get; init; }
    public bool SelfContained { get; init; }
    public bool IncludeDependencies { get; init; }
    public bool Optimize { get; init; }
    public bool Compression { get; init; }
    public IReadOnlyList<ArchitectureKind> Architectures { get; init; } = [ArchitectureKind.X64];
    public IReadOnlyList<BuildMethod> Methods { get; init; } = [];
    public string MethodExplanation { get; init; } = "";
}

public sealed class AppSettings
{
    public string DefaultOutputDirectory { get; set; } = "";
    public ArchitectureKind DefaultArchitecture { get; set; } = ArchitectureKind.X64;
    public string Theme { get; set; } = "Holographic";
    public string Language { get; set; } = "en";
    public bool KeepBuildLogs { get; set; } = true;
    public List<string> RecentProjects { get; set; } = [];
}
