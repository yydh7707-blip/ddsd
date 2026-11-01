using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class CppBuilder
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;

    public CppBuilder(ProcessRunner runner, BuildVerifier verifier)
    {
        _runner = runner;
        _verifier = verifier;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string>();
        void Line(string text) { log.Add(text); }
        if (request.Analysis.RequiresMsvc && request.Options.Method == BuildMethod.MinGw)
            return BuildVerifier.Fail(Text.T("이 프로젝트는 MSVC가 필요합니다. MinGW로 대체하지 않습니다.",
                "This project requires MSVC. It is not retargeted to MinGW."), "", Elapsed(started), BuildStage.ConfiguringBuild);

        var platform = request.Options.Architecture switch
        {
            ArchitectureKind.X86 => "Win32",
            ArchitectureKind.Arm64 => "ARM64",
            _ => "x64"
        };
        ProcessResult result;
        string outRoot;
        if (request.Options.Method == BuildMethod.MinGw)
        {
            if (!request.Tools.TryGetValue("mingw", out var gcc) || gcc.ExecutablePath is null || !request.Tools.TryGetValue("cmake", out var cmakeMingw) || cmakeMingw.ExecutablePath is null)
                return BuildVerifier.Fail(Text.T("MinGW 또는 CMake가 없습니다.", "MinGW or CMake is missing."), "", Elapsed(started), BuildStage.CheckingRequirements);
            outRoot = Path.Combine(workspace, "dex-mingw");
            result = await _runner.RunAsync(cmakeMingw.ExecutablePath, ["-S", workspace, "-B", outRoot, "-G", "MinGW Makefiles", "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_C_COMPILER=" + gcc.ExecutablePath], workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
            if (result.ExitCode == 0)
                result = await _runner.RunAsync(cmakeMingw.ExecutablePath, ["--build", outRoot], workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
        }
        else if (request.Options.Method == BuildMethod.CMake || File.Exists(Path.Combine(workspace, "CMakeLists.txt")))
        {
            if (!request.Tools.TryGetValue("cmake", out var cmake) || cmake.ExecutablePath is null)
                return BuildVerifier.Fail(Text.T("CMake가 없습니다.", "CMake is missing."), "", Elapsed(started), BuildStage.CheckingRequirements);
            if (!request.Tools.TryGetValue("vs-buildtools", out var vs) || vs.State != InstallState.Installed)
                return BuildVerifier.Fail(Text.T("CMake Visual Studio 생성기에는 C++ Build Tools가 필요합니다.",
                    "The CMake Visual Studio generator needs the C++ Build Tools."), "", Elapsed(started), BuildStage.CheckingRequirements);
            outRoot = Path.Combine(workspace, "dex-cmake");
            var arch = request.Options.Architecture switch { ArchitectureKind.X86 => "Win32", ArchitectureKind.Arm64 => "ARM64", _ => "x64" };
            result = await _runner.RunAsync(cmake.ExecutablePath, ["-S", workspace, "-B", outRoot, "-A", arch], workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
            if (result.ExitCode == 0)
                result = await _runner.RunAsync(cmake.ExecutablePath, ["--build", outRoot, "--config", request.Options.Optimize ? "Release" : "Debug"], workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
        }
        else
        {
            if (!request.Tools.TryGetValue("vs-buildtools", out var vs) || vs.ExecutablePath is null)
                return BuildVerifier.Fail(Text.T("MSBuild를 찾지 못했습니다.", "MSBuild was not found."), "", Elapsed(started), BuildStage.CheckingRequirements);
            var project = Directory.EnumerateFiles(workspace, "*.vcxproj", SearchOption.AllDirectories).FirstOrDefault();
            if (project is null)
                return BuildVerifier.Fail(Text.T("vcxproj가 없습니다.", "No vcxproj was found."), "", Elapsed(started), BuildStage.Preparing);
            outRoot = Path.GetDirectoryName(project)!;
            result = await _runner.RunAsync(vs.ExecutablePath, [project, "/p:Configuration=" + (request.Options.Optimize ? "Release" : "Debug"), "/p:Platform=" + platform], workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
        }

        var text = string.Join(Environment.NewLine, log);
        if (result.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("C++ 빌드가 실패했습니다.", "The C++ build failed."), text, Elapsed(started), BuildStage.Compiling);
        var exe = Directory.EnumerateFiles(outRoot, "*.exe", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains("CMake", StringComparison.OrdinalIgnoreCase));
        return _verifier.Verify(exe, outRoot, OutputKind.SingleFileExecutable, text, Elapsed(started), [], Text.Method(request.Options.Method));
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
