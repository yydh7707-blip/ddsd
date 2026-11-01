using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class GoBuilder
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;

    public GoBuilder(ProcessRunner runner, BuildVerifier verifier)
    {
        _runner = runner;
        _verifier = verifier;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string>();
        if (!request.Tools.TryGetValue("go", out var go) || go.ExecutablePath is null)
            return BuildVerifier.Fail(Text.T("Go SDK가 없습니다.", "Go SDK is missing."), "", Elapsed(started), BuildStage.CheckingRequirements);
        if (request.Analysis.EntryPoint is null && request.Options.SelectedEntry is null)
            return BuildVerifier.Fail(Text.T("package main 진입점이 없습니다.", "No package main entry point."), "", Elapsed(started), BuildStage.Preparing);
        var name = SecurityManager.SanitizeFileName(request.Options.OutputName);
        var outDir = Path.Combine(workspace, "dex-out");
        Directory.CreateDirectory(outDir);
        var exe = Path.Combine(outDir, name + ".exe");
        var env = new Dictionary<string, string>
        {
            ["GOOS"] = "windows",
            ["GOARCH"] = BuildWorkspace.GoArch(request.Options.Architecture),
            ["CGO_ENABLED"] = "0"
        };
        progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = "go build" });
        var result = await _runner.RunAsync(go.ExecutablePath, ["build", "-o", exe, "."], workspace, env, SecurityManager.BuildTimeout, 2_000_000, line => log.Add(line), false, cancellationToken);
        if (result.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("go build가 실패했습니다.", "go build failed."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Compiling);
        return _verifier.Verify(exe, outDir, OutputKind.SingleFileExecutable, string.Join(Environment.NewLine, log), Elapsed(started), [], "go build");
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
