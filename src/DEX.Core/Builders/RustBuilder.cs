using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class RustBuilder
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;

    public RustBuilder(ProcessRunner runner, BuildVerifier verifier)
    {
        _runner = runner;
        _verifier = verifier;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string>();
        if (!request.Tools.TryGetValue("rust", out var rust) || rust.ExecutablePath is null)
            return BuildVerifier.Fail(Text.T("Cargo를 찾지 못했습니다.", "Cargo was not found."), "", Elapsed(started), BuildStage.CheckingRequirements);
        var cargo = rust.ExecutablePath.EndsWith("cargo.exe", StringComparison.OrdinalIgnoreCase)
            ? rust.ExecutablePath
            : Path.Combine(Path.GetDirectoryName(rust.ExecutablePath)!, "cargo.exe");
        if (!File.Exists(cargo))
            return BuildVerifier.Fail(Text.T("cargo.exe가 없습니다.", "cargo.exe is missing."), "", Elapsed(started), BuildStage.CheckingRequirements);
        if (!request.Tools.TryGetValue("vs-buildtools", out var linker) || linker.State != InstallState.Installed)
            return BuildVerifier.Fail(Text.T("Windows 링커가 없습니다. Visual Studio Build Tools를 설치한 뒤 재검사하세요.",
                "The Windows linker is missing. Install Visual Studio Build Tools and recheck."), "", Elapsed(started), BuildStage.CheckingRequirements);
        var target = BuildWorkspace.RustTarget(request.Options.Architecture);
        var rustup = Path.Combine(Path.GetDirectoryName(cargo)!, "rustup.exe");
        if (File.Exists(rustup))
        {
            var listed = await _runner.RunAsync(rustup, ["target", "list", "--installed"], workspace, null, TimeSpan.FromSeconds(30), 100_000, line => log.Add(line), false, cancellationToken);
            if (!listed.StandardOutput.Contains(target, StringComparison.OrdinalIgnoreCase))
                return BuildVerifier.Fail(Text.T($"Rust 타깃 {target} 이 설치되어 있지 않습니다. DEX는 rustup target add를 실행하지 않습니다. 공식 설치 안내를 확인하세요.",
                    $"Rust target {target} is not installed. DEX does not run rustup target add. See the official install guide."),
                    string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.CheckingRequirements);
        }
        var manifest = Path.Combine(workspace, "Cargo.toml");
        progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = "cargo build --release" });
        var args = new List<string> { "build", "--manifest-path", manifest, "--target", target };
        if (request.Options.Optimize)
            args.Add("--release");
        var result = await _runner.RunAsync(cargo, args, workspace, null, SecurityManager.BuildTimeout, 2_000_000, line => log.Add(line), false, cancellationToken);
        if (result.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("cargo build가 실패했습니다.", "cargo build failed."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Compiling);
        var profile = request.Options.Optimize ? "release" : "debug";
        var exe = Directory.EnumerateFiles(Path.Combine(workspace, "target", target, profile), "*.exe").FirstOrDefault();
        return _verifier.Verify(exe, Path.GetDirectoryName(exe) ?? workspace, OutputKind.SingleFileExecutable, string.Join(Environment.NewLine, log), Elapsed(started), [], "cargo");
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
