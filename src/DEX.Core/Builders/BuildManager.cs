using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class BuildManager
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;
    private readonly BuildWorkspace _workspace = new();
    private readonly DotNetBuilder _dotnet;
    private readonly PythonBuilder _python;
    private readonly WebAppBuilder _web;
    private readonly CppBuilder _cpp;
    private readonly JavaBuilder _java;
    private readonly GoBuilder _go;
    private readonly RustBuilder _rust;
    private readonly PhpPackager _php;
    private readonly RubyPackager _ruby;
    private readonly ScriptPackager _script;

    public BuildManager(ProcessRunner runner, BuildVerifier verifier, ToolLocator locator)
    {
        _runner = runner;
        _verifier = verifier;
        _dotnet = new DotNetBuilder(runner, verifier);
        _python = new PythonBuilder(runner, verifier);
        _web = new WebAppBuilder(runner, verifier, locator);
        _cpp = new CppBuilder(runner, verifier);
        _java = new JavaBuilder(runner, verifier);
        _go = new GoBuilder(runner, verifier);
        _rust = new RustBuilder(runner, verifier);
        _php = new PhpPackager(runner, verifier);
        _ruby = new RubyPackager(runner, verifier);
        _script = new ScriptPackager(runner, verifier);
    }

    public static bool WindowsSandboxPresent =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsSandbox.exe"));

    public async Task<BuildResult> BuildAsync(BuildRequest request, IProgress<BuildProgress>? progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        progress ??= new Progress<BuildProgress>();
        if (!request.ConsentGranted)
        {
            return BuildVerifier.Fail(Text.T("빌드 스크립트는 임의 명령을 실행할 수 있습니다. 격리된 작업 복사본에서 진행하려면 동의가 필요합니다. Windows Sandbox 자동 실행은 사용하지 않습니다.",
                "Build scripts can run arbitrary commands. Consent is required to continue in an isolated working copy. Windows Sandbox is not launched automatically."),
                "", TimeSpan.Zero, BuildStage.Preparing);
        }

        progress.Report(new BuildProgress { Stage = BuildStage.Preparing, Message = Text.Stage(BuildStage.Preparing) });
        string workspace;
        try
        {
            workspace = _workspace.CreateCopy(request.ProjectRoot);
        }
        catch (Exception ex)
        {
            return BuildVerifier.Fail(ex.Message, ex.Message, DateTimeOffset.UtcNow - started, BuildStage.Preparing);
        }

        try
        {
            return await BuildInWorkspaceAsync(request, workspace, progress, started, cancellationToken);
        }
        finally
        {
            _workspace.DeleteCopy(workspace);
        }
    }

    private async Task<BuildResult> BuildInWorkspaceAsync(
        BuildRequest request,
        string workspace,
        IProgress<BuildProgress> progress,
        DateTimeOffset started,
        CancellationToken cancellationToken)
    {
        progress.Report(new BuildProgress { Stage = BuildStage.CheckingRequirements, Message = Text.Stage(BuildStage.CheckingRequirements) });
        var method = request.Options.Method == BuildMethod.None ? request.Analysis.RecommendedMethod : request.Options.Method;
        var options = request.Options;
        options.Method = method;
        var forwarded = new BuildRequest
        {
            ProjectRoot = workspace,
            Analysis = request.Analysis,
            Options = options,
            Tools = request.Tools,
            ConsentGranted = true
        };

        BuildResult result;
        try
        {
            result = method switch
            {
                BuildMethod.PyInstaller or BuildMethod.Nuitka => await _python.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.WebView2 or BuildMethod.Electron => await _web.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.DotNetPublish => await _dotnet.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.MsBuild or BuildMethod.CMake or BuildMethod.MinGw => await _cpp.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.Jpackage => await _java.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.GoBuild => await _go.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.Cargo => await _rust.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.PhpHost => await _php.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.RubyHost => await _ruby.BuildAsync(forwarded, workspace, progress, cancellationToken),
                BuildMethod.ScriptWrapper => await _script.BuildAsync(forwarded, workspace, progress, cancellationToken),
                _ => BuildVerifier.Fail(Text.T("이 프로젝트에는 빌드 방법이 없습니다.", "This project has no build method."), "", DateTimeOffset.UtcNow - started, BuildStage.ConfiguringBuild)
            };
        }
        catch (Exception ex)
        {
            result = BuildVerifier.Fail(ex.Message, ex.ToString(), DateTimeOffset.UtcNow - started, BuildStage.Failed);
        }

        if (result.Success && result.ExecutablePath is not null)
        {
            try
            {
                var destination = CopyToOutput(request.Options.OutputDirectory, result);
                result = new BuildResult
                {
                    Success = true,
                    ExecutablePath = destination.exe,
                    OutputDirectory = destination.dir,
                    OutputKind = result.OutputKind,
                    Architecture = result.Architecture,
                    Log = result.Log,
                    Duration = DateTimeOffset.UtcNow - started,
                    Warnings = result.Warnings,
                    OutputDescription = result.OutputDescription
                };
            }
            catch (Exception ex)
            {
                result = BuildVerifier.Fail(Text.T("검증은 통과했지만 지정한 위치에 저장하지 못했습니다: ", "Verification passed but saving to the chosen location failed: ") + ex.Message,
                    result.Log, DateTimeOffset.UtcNow - started, BuildStage.Verifying);
            }
        }

        progress.Report(new BuildProgress
        {
            Stage = result.Success ? BuildStage.Completed : BuildStage.Failed,
            Message = result.Success ? Text.Stage(BuildStage.Completed) : result.Error ?? Text.Stage(BuildStage.Failed),
            IsError = !result.Success
        });
        return result;
    }

    private static (string exe, string dir) CopyToOutput(string outputDirectory, BuildResult result)
    {
        Directory.CreateDirectory(outputDirectory);
        var sourceDir = result.OutputDirectory ?? Path.GetDirectoryName(result.ExecutablePath)!;
        var folderOutput = result.OutputKind is OutputKind.ApplicationFolder or OutputKind.RuntimeHost or OutputKind.Wrapper;
        if (!folderOutput)
        {
            var target = Path.Combine(outputDirectory, Path.GetFileName(result.ExecutablePath!));
            File.Copy(result.ExecutablePath!, target, true);
            return (target, outputDirectory);
        }

        var dest = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(result.ExecutablePath!));
        if (Directory.Exists(dest))
            Directory.Delete(dest, true);
        CopyDirectory(sourceDir, dest);
        var exe = Path.Combine(dest, Path.GetFileName(result.ExecutablePath!));
        if (!File.Exists(exe))
            throw new FileNotFoundException("Copied EXE is missing.", exe);
        return (exe, dest);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
