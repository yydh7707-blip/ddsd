using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class PhpPackager
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;
    public PhpPackager(ProcessRunner runner, BuildVerifier verifier) { _runner = runner; _verifier = verifier; }

    public Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken) =>
        HostBuild.BuildAsync(_runner, _verifier, request, workspace, progress, cancellationToken, "php", "php.exe", OutputKind.RuntimeHost,
            Text.T("PHP 런타임을 포함하는 호스트입니다. 네이티브 컴파일이 아닙니다.", "Host includes the PHP runtime. This is not a native compile."),
            """
            var root = AppContext.BaseDirectory;
            var runtime = Path.Combine(root, "runtime", "php.exe");
            var script = Directory.EnumerateFiles(Path.Combine(root, "app"), "*.php").FirstOrDefault();
            if (script is null || !File.Exists(runtime)) { Console.Error.WriteLine("PHP runtime or script missing"); return 1; }
            var psi = new System.Diagnostics.ProcessStartInfo(runtime, script) { UseShellExecute = false, WorkingDirectory = Path.Combine(root, "app") };
            return System.Diagnostics.Process.Start(psi)!.WaitForExitAsync().ContinueWith(_ => 0).GetAwaiter().GetResult();
            """);
}

public sealed class RubyPackager
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;
    public RubyPackager(ProcessRunner runner, BuildVerifier verifier) { _runner = runner; _verifier = verifier; }

    public Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken) =>
        HostBuild.BuildAsync(_runner, _verifier, request, workspace, progress, cancellationToken, "ruby", "ruby.exe", OutputKind.RuntimeHost,
            Text.T("실험적 Ruby 런타임 호스트입니다. 검증된 네이티브 패키징이 아닙니다.", "Experimental Ruby runtime host. Not a verified native package."),
            """
            var root = AppContext.BaseDirectory;
            var runtime = Path.Combine(root, "runtime", "ruby.exe");
            var script = Directory.EnumerateFiles(Path.Combine(root, "app"), "*.rb").FirstOrDefault();
            if (script is null || !File.Exists(runtime)) { Console.Error.WriteLine("Ruby runtime or script missing"); return 1; }
            var psi = new System.Diagnostics.ProcessStartInfo(runtime, script) { UseShellExecute = false };
            return System.Diagnostics.Process.Start(psi)!.WaitForExitAsync().ContinueWith(_ => 0).GetAwaiter().GetResult();
            """);
}

public sealed class ScriptPackager
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;
    public ScriptPackager(ProcessRunner runner, BuildVerifier verifier) { _runner = runner; _verifier = verifier; }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var entry = request.Options.SelectedEntry ?? request.Analysis.EntryPoint;
        if (string.IsNullOrWhiteSpace(entry))
            return BuildVerifier.Fail(Text.T("래퍼로 실행할 스크립트가 없습니다.", "No script to wrap."), "", TimeSpan.Zero, BuildStage.Preparing);
        var ext = Path.GetExtension(entry).ToLowerInvariant();
        var launcher = ext == ".ps1"
            ? """
              var script = Path.Combine(AppContext.BaseDirectory, "app", "entry.ps1");
              var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"") { UseShellExecute = false };
              return System.Diagnostics.Process.Start(psi)!.WaitForExitAsync().ContinueWith(_ => 0).GetAwaiter().GetResult();
              """
            : """
              var script = Path.Combine(AppContext.BaseDirectory, "app", "entry.cmd");
              var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c call \"" + script + "\"") { UseShellExecute = false };
              return System.Diagnostics.Process.Start(psi)!.WaitForExitAsync().ContinueWith(_ => 0).GetAwaiter().GetResult();
              """;
        return await HostBuild.BuildAsync(_runner, _verifier, request, workspace, progress, cancellationToken, null, null, OutputKind.Wrapper,
            Text.T("실행 래퍼입니다. 스크립트를 네이티브 코드로 컴파일하지 않았습니다.", "Execution wrapper. The script was not compiled to native code."),
            launcher, entry);
    }
}

internal static class HostBuild
{
    public static async Task<BuildResult> BuildAsync(
        ProcessRunner runner, BuildVerifier verifier, BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken,
        string? toolId, string? runtimeExe, OutputKind kind, string description, string body, string? onlyEntry = null)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string> { description };
        if (!request.Tools.TryGetValue("dotnet-sdk", out var sdk) || sdk.ExecutablePath is null)
            return BuildVerifier.Fail(Text.T(".NET SDK가 없습니다.", ".NET SDK is missing."), description, TimeSpan.Zero, BuildStage.CheckingRequirements);
        var name = SecurityManager.SanitizeFileName(request.Options.OutputName);
        var host = Path.Combine(workspace, "dex-host");
        var app = Path.Combine(host, "app");
        Directory.CreateDirectory(app);
        if (onlyEntry is null)
        {
            foreach (var file in Directory.EnumerateFiles(workspace))
            {
                if (file.Contains("dex-host", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(file, Path.Combine(app, Path.GetFileName(file)), true);
            }
        }
        else
        {
            var source = Path.Combine(workspace, onlyEntry);
            var destName = Path.GetExtension(onlyEntry).Equals(".ps1", StringComparison.OrdinalIgnoreCase) ? "entry.ps1" : "entry.cmd";
            File.Copy(source, Path.Combine(app, destName), true);
        }

        if (toolId is not null)
        {
            if (!request.Tools.TryGetValue(toolId, out var tool) || tool.ExecutablePath is null)
                return BuildVerifier.Fail(Text.T("포함할 런타임을 찾지 못했습니다.", "Runtime to include was not found."), description, Elapsed(started), BuildStage.CheckingRequirements);
            var runtimeDir = Path.GetDirectoryName(tool.ExecutablePath)!;
            var size = Directory.EnumerateFiles(runtimeDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            if (size > 400L * 1024 * 1024)
                return BuildVerifier.Fail(Text.T("런타임 폴더가 너무 커서 복사하지 않았습니다.", "Runtime folder is too large to copy."), description, Elapsed(started), BuildStage.Packaging);
            CopyDir(runtimeDir, Path.Combine(host, "runtime"));
            if (!File.Exists(Path.Combine(host, "runtime", runtimeExe!)))
                return BuildVerifier.Fail(Text.T("런타임 실행 파일을 복사하지 못했습니다.", "Runtime executable was not copied."), description, Elapsed(started), BuildStage.Packaging);
        }

        var runtimeItem = toolId is null
                    ? ""
                    : """<Content Include="runtime\**" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />""";
                var csproj = $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <AssemblyName>{{name}}</AssemblyName>
                    <PublishSingleFile>true</PublishSingleFile>
                    <SelfContained>true</SelfContained>
                    <RuntimeIdentifier>{{BuildWorkspace.Rid(request.Options.Architecture)}}</RuntimeIdentifier>
                  </PropertyGroup>
                  <ItemGroup>
                    <Content Include="app\**" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />
                    {{runtimeItem}}
                  </ItemGroup>
                </Project>
                """;
                var program = "internal static class Program\n{\n    public static int Main()\n    {\n" + body + "\n    }\n}\n";
        var outDir = Path.Combine(host, "publish");
        progress.Report(new BuildProgress { Stage = BuildStage.Packaging, Message = description, LogLine = description });
        var result = await BuildWorkspace.PublishGeneratedAsync(runner, sdk.ExecutablePath, host, outDir, csproj, program, line => log.Add(line), cancellationToken);
        if (result.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("호스트 컴파일이 실패했습니다.", "Host compilation failed."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Compiling);
        return verifier.Verify(Path.Combine(outDir, name + ".exe"), outDir, kind, string.Join(Environment.NewLine, log), Elapsed(started), [description], description);
    }

    private static void CopyDir(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDir(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
