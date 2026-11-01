namespace DEX.Core.Models;

public sealed class BuildProgress
{
    public required BuildStage Stage { get; init; }
    public required string Message { get; init; }
    public string? LogLine { get; init; }
    public bool IsError { get; init; }
    public bool IsWarning { get; init; }
}

public sealed class BuildResult
{
    public required bool Success { get; init; }
    public string? ExecutablePath { get; init; }
    public string? OutputDirectory { get; init; }
    public OutputKind OutputKind { get; init; }
    public string? Architecture { get; init; }
    public string? FailureStage { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string Log { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public string OutputDescription { get; init; } = "";
}

public sealed class HistoryEntry
{
    public required string Id { get; init; }
    public required string ProjectName { get; init; }
    public required string Language { get; init; }
    public string? Framework { get; init; }
    public required string BuildTool { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required double DurationSeconds { get; init; }
    public required bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ProjectPath { get; init; }
    public string? LogPath { get; init; }
    public string? Error { get; init; }
    public BuildMethod Method { get; init; }
}

public sealed class IntakeResult
{
    public required string WorkspacePath { get; init; }
    public required string DisplayName { get; init; }
    public required bool FromArchive { get; init; }
    public required string OriginalPath { get; init; }
}

public sealed class BuildRequest
{
    public required string ProjectRoot { get; init; }
    public required ProjectAnalysis Analysis { get; init; }
    public required BuildOptions Options { get; init; }
    public required IReadOnlyDictionary<string, ToolStatus> Tools { get; init; }
    public required bool ConsentGranted { get; init; }
}

public sealed class PlannedCommand
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string Description { get; init; }
}
