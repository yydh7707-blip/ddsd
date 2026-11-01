using DEX.Core.Builders;
using DEX.Core.History;
using DEX.Core.Models;

namespace DEX.Core.Services;

public sealed class BuildService
{
    private readonly BuildManager _manager;
    private readonly HistoryService _history;

    public BuildService(BuildManager manager, HistoryService history)
    {
        _manager = manager;
        _history = history;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var result = await _manager.BuildAsync(request, progress, cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        var logPath = _history.SaveLog(id, result.Log);
        _history.Add(new HistoryEntry
        {
            Id = id,
            ProjectName = request.Analysis.ProjectName,
            Language = request.Analysis.LanguageName,
            Framework = request.Analysis.Framework,
            BuildTool = DEX.Core.Localization.Text.Method(request.Options.Method),
            StartedAt = started,
            DurationSeconds = result.Duration.TotalSeconds,
            Success = result.Success,
            OutputPath = result.ExecutablePath,
            ProjectPath = request.Analysis.RootPath,
            LogPath = logPath,
            Error = result.Error,
            Method = request.Options.Method
        });
        return result;
    }
}
