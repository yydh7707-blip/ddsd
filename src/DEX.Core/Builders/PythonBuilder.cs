using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class PythonBuilder
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;

    public PythonBuilder(ProcessRunner runner, BuildVerifier verifier)
    {
        _runner = runner;
        _verifier = verifier;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string>();
        void Line(string text) { log.Add(text); progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = text, LogLine = text }); }
        string Joined() => string.Join(Environment.NewLine, log);

        if (!request.Tools.TryGetValue("python", out var python) || string.IsNullOrWhiteSpace(python.ExecutablePath))
            return BuildVerifier.Fail(Text.T("Python이 설치되어 있지 않습니다.", "Python is not installed."), "", Elapsed(started), BuildStage.CheckingRequirements);

        var entryRel = request.Options.SelectedEntry ?? request.Analysis.EntryPoint;
        if (string.IsNullOrWhiteSpace(entryRel))
            return BuildVerifier.Fail(Text.T("Python 진입점이 없습니다.", "Python entry point is missing."), Joined(), Elapsed(started), BuildStage.Preparing);
        var entry = Path.Combine(workspace, entryRel);
        if (!File.Exists(entry))
            return BuildVerifier.Fail(Text.T("진입점 파일을 찾지 못했습니다.", "Entry file was not found."), Joined(), Elapsed(started), BuildStage.Preparing);
        if (entry.EndsWith(".pyc", StringComparison.OrdinalIgnoreCase))
            return BuildVerifier.Fail(Text.T(".pyc만으로는 빌드하지 않습니다.", "A .pyc alone is not built."), Joined(), Elapsed(started), BuildStage.Preparing);

        var venv = Path.Combine(workspace, ".dex-venv");
        progress.Report(new BuildProgress { Stage = BuildStage.ResolvingDependencies, Message = Text.Stage(BuildStage.ResolvingDependencies) });
        var venvResult = await _runner.RunAsync(python.ExecutablePath, ["-m", "venv", venv], workspace, null, TimeSpan.FromMinutes(3), 200_000, Line, false, cancellationToken);
        if (venvResult.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("가상 환경 생성에 실패했습니다.", "Could not create a virtual environment."), Joined(), Elapsed(started), BuildStage.ResolvingDependencies);
        var vpy = Path.Combine(venv, "Scripts", "python.exe");
        if (!File.Exists(vpy))
            return BuildVerifier.Fail(Text.T("가상 환경의 python.exe가 없습니다.", "venv python.exe is missing."), Joined(), Elapsed(started), BuildStage.ResolvingDependencies);

        if (request.Options.IncludeDependencies)
        {
            var requirements = Path.Combine(workspace, "requirements.txt");
            if (File.Exists(requirements))
            {
                var pip = await _runner.RunAsync(vpy, ["-m", "pip", "install", "-r", requirements], workspace, null, TimeSpan.FromMinutes(20), 2_000_000, Line, false, cancellationToken);
                if (pip.ExitCode != 0)
                    return BuildVerifier.Fail(Text.T("requirements.txt 설치에 실패했습니다.", "Installing requirements.txt failed."), Joined(), Elapsed(started), BuildStage.ResolvingDependencies);
            }
        }

        var tool = request.Options.Method == BuildMethod.Nuitka ? "nuitka" : "pyinstaller";
        var installTool = await _runner.RunAsync(vpy, ["-m", "pip", "install", tool], workspace, null, TimeSpan.FromMinutes(20), 2_000_000, Line, false, cancellationToken);
        if (installTool.ExitCode != 0)
            return BuildVerifier.Fail(Text.T($"{tool} 설치에 실패했습니다.", $"Installing {tool} failed."), Joined(), Elapsed(started), BuildStage.ResolvingDependencies);

        if (request.Options.Method == BuildMethod.Nuitka)
        {
            var gcc = request.Tools.TryGetValue("mingw", out var mingw) && mingw.State == InstallState.Installed;
            var msvc = request.Tools.TryGetValue("vs-buildtools", out var vs) && vs.State == InstallState.Installed;
            if (!gcc && !msvc)
                return BuildVerifier.Fail(Text.T("Nuitka에는 C/C++ 컴파일러가 필요합니다. MinGW로 대체할 수 없는 경우 설치 후 재검사하세요.",
                    "Nuitka needs a C/C++ compiler. Recheck after installing one."), Joined(), Elapsed(started), BuildStage.CheckingRequirements);
        }

        var name = SecurityManager.SanitizeFileName(request.Options.OutputName);
        var dist = Path.Combine(workspace, "dex-dist");
        Directory.CreateDirectory(dist);
        progress.Report(new BuildProgress { Stage = BuildStage.Packaging, Message = Text.Stage(BuildStage.Packaging) });
        List<string> args;
        if (request.Options.Method == BuildMethod.Nuitka)
        {
            args = ["-m", "nuitka", "--onefile", "--output-dir=" + dist, "--output-filename=" + name + ".exe", entry];
            if (!request.Options.Console) args.Insert(3, "--windows-console-mode=disable");
        }
        else
        {
            args =
            [
                "-m", "PyInstaller", "--noconfirm", "--clean",
                request.Options.SingleFile ? "--onefile" : "--onedir",
                "--name", name,
                "--distpath", dist,
                "--workpath", Path.Combine(workspace, "dex-work"),
                "--specpath", Path.Combine(workspace, "dex-spec")
            ];
            if (!request.Options.Console || entry.EndsWith(".pyw", StringComparison.OrdinalIgnoreCase))
                args.Add("--windowed");
            if (!string.IsNullOrWhiteSpace(request.Options.IconPath) && File.Exists(request.Options.IconPath))
            {
                args.Add("--icon");
                args.Add(request.Options.IconPath);
            }
            args.Add(entry);
        }

        var build = await _runner.RunAsync(vpy, args, workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
        if (build.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("Python 패키징이 실패했습니다.", "Python packaging failed."), Joined(), Elapsed(started), BuildStage.Packaging);
        var exe = Directory.EnumerateFiles(dist, name + ".exe", SearchOption.AllDirectories).FirstOrDefault();
        return _verifier.Verify(exe, dist, OutputKind.SingleFileExecutable, Joined(), Elapsed(started),
            [Text.T("PyInstaller/Nuitka 출력은 패키징 결과입니다.", "PyInstaller/Nuitka output is a package, not a renamed source file.")],
            Text.Method(request.Options.Method));
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
