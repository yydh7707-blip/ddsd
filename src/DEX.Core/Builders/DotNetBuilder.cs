using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class DotNetBuilder
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;

    public DotNetBuilder(ProcessRunner runner, BuildVerifier verifier)
    {
        _runner = runner;
        _verifier = verifier;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string>();
        void Line(string text) { log.Add(text); progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = text, LogLine = text }); }

        if (!request.Tools.TryGetValue("dotnet-sdk", out var sdk) || string.IsNullOrWhiteSpace(sdk.ExecutablePath) || !File.Exists(sdk.ExecutablePath))
            return BuildVerifier.Fail(Text.T(".NET SDK를 찾지 못했습니다.", ".NET SDK was not found."), "", TimeSpan.Zero, BuildStage.CheckingRequirements);

        var entry = request.Options.SelectedEntry ?? request.Analysis.EntryPoint;
        var project = string.IsNullOrWhiteSpace(entry)
            ? Directory.EnumerateFiles(workspace, "*.csproj", SearchOption.AllDirectories).FirstOrDefault()
            : Path.Combine(workspace, entry);
        if (project is null || !File.Exists(project))
            return BuildVerifier.Fail(Text.T("빌드할 프로젝트 파일이 없습니다.", "No project file to build."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Preparing);

        progress.Report(new BuildProgress { Stage = BuildStage.ConfiguringBuild, Message = Text.Stage(BuildStage.ConfiguringBuild) });
        var name = SecurityManager.SanitizeFileName(request.Options.OutputName);
        var outDir = Path.Combine(workspace, "dex-out");
        Directory.CreateDirectory(outDir);
        var args = new List<string>
        {
            "publish", project,
            "-c", request.Options.Optimize ? "Release" : "Debug",
            "-r", BuildWorkspace.Rid(request.Options.Architecture),
            "--self-contained", request.Options.SelfContained ? "true" : "false",
            "-p:PublishSingleFile=" + (request.Options.SingleFile ? "true" : "false"),
            "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:EnableCompressionInSingleFile=" + (request.Options.Compression ? "true" : "false"),
            "-p:AssemblyName=" + name,
            "-o", outDir
        };
        if (SecurityManager.IsVersionToken(request.Options.ProductVersion))
        {
            args.Add("-p:Version=" + request.Options.ProductVersion);
            args.Add("-p:ProductVersion=" + request.Options.ProductVersion);
        }
        if (!string.IsNullOrWhiteSpace(request.Options.ProductName))
            args.Add("-p:Product=" + SecurityManager.SanitizeFileName(request.Options.ProductName));
        if (!string.IsNullOrWhiteSpace(request.Options.CompanyName))
            args.Add("-p:Company=" + SecurityManager.SanitizeFileName(request.Options.CompanyName));
        if (!string.IsNullOrWhiteSpace(request.Options.IconPath) && File.Exists(request.Options.IconPath))
            args.Add("-p:ApplicationIcon=" + request.Options.IconPath);
        if (!request.Options.Console)
            args.Add("-p:OutputType=WinExe");

        progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = Text.Stage(BuildStage.Compiling) });
        var result = await _runner.RunAsync(sdk.ExecutablePath, args, Path.GetDirectoryName(project), null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
        var text = string.Join(Environment.NewLine, log);
        if (result.ExitCode != 0 || result.TimedOut)
            return BuildVerifier.Fail(result.TimedOut
                ? Text.T("빌드 시간이 초과되었습니다.", "Build timed out.")
                : Text.T("dotnet publish가 실패했습니다.", "dotnet publish failed."), text + result.StandardError, Elapsed(started), BuildStage.Compiling);

        progress.Report(new BuildProgress { Stage = BuildStage.Verifying, Message = Text.Stage(BuildStage.Verifying) });
        var exe = Directory.EnumerateFiles(outDir, name + ".exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? Directory.EnumerateFiles(outDir, "*.exe").FirstOrDefault(f => !f.EndsWith("createdump.exe", StringComparison.OrdinalIgnoreCase));
        return _verifier.Verify(exe, outDir, request.Options.SingleFile ? OutputKind.SingleFileExecutable : OutputKind.ApplicationFolder, text, Elapsed(started), [],
            request.Options.SelfContained
                ? Text.T("self-contained 게시입니다.", "Self-contained publish.")
                : Text.T("framework-dependent 게시입니다. 최종 PC에 해당 런타임이 필요합니다.", "Framework-dependent publish. The target PC needs the matching runtime."));
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
