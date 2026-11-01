using System.Text.Json;
using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class WebAppBuilder
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;
    private readonly ToolLocator _locator;

    public WebAppBuilder(ProcessRunner runner, BuildVerifier verifier, ToolLocator locator)
    {
        _runner = runner;
        _verifier = verifier;
        _locator = locator;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string>();
        void Line(string text) { log.Add(text); progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = text, LogLine = text }); }
        string Joined() => string.Join(Environment.NewLine, log);

        if (!request.Tools.TryGetValue("dotnet-sdk", out var sdk) || sdk.ExecutablePath is null)
            return BuildVerifier.Fail(Text.T("호스트를 컴파일할 .NET SDK가 없습니다.", ".NET SDK is required to compile the host."), "", Elapsed(started), BuildStage.CheckingRequirements);

        var content = workspace;
        var packageJson = Path.Combine(workspace, "package.json");
        if (File.Exists(packageJson))
        {
            if (!request.Tools.TryGetValue("node", out var node) || node.ExecutablePath is null)
                return BuildVerifier.Fail(Text.T("Node.js가 없습니다.", "Node.js is missing."), Joined(), Elapsed(started), BuildStage.CheckingRequirements);
            var cli = _locator.FindNpmCli(node.ExecutablePath);
            if (cli is null)
                return BuildVerifier.Fail(Text.T("npm-cli.js를 찾지 못했습니다.", "npm-cli.js was not found."), Joined(), Elapsed(started), BuildStage.CheckingRequirements);
            progress.Report(new BuildProgress { Stage = BuildStage.ResolvingDependencies, Message = Text.Stage(BuildStage.ResolvingDependencies) });
            if (request.Options.IncludeDependencies)
            {
                var install = await _runner.RunAsync(node.ExecutablePath, [cli, "install", "--ignore-scripts"], workspace, null, TimeSpan.FromMinutes(20), 2_000_000, Line, false, cancellationToken);
                if (install.ExitCode != 0)
                    return BuildVerifier.Fail(Text.T("npm install이 실패했습니다. 설치 스크립트는 실행하지 않았습니다.", "npm install failed. Install scripts were not run."), Joined(), Elapsed(started), BuildStage.ResolvingDependencies);
            }
            if (HasScript(packageJson, "build"))
            {
                progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = Text.Stage(BuildStage.Compiling) });
                var build = await _runner.RunAsync(node.ExecutablePath, [cli, "run", "build"], workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
                if (build.ExitCode != 0)
                    return BuildVerifier.Fail(Text.T("웹 프로젝트 빌드가 실패했습니다.", "The web project build failed."), Joined(), Elapsed(started), BuildStage.Compiling);
            }
            content = new[] { "dist", "build", "out" }.Select(d => Path.Combine(workspace, d)).FirstOrDefault(Directory.Exists) ?? workspace;
        }

        var index = Directory.EnumerateFiles(content, "index.html", SearchOption.AllDirectories).FirstOrDefault();
        if (index is null)
            return BuildVerifier.Fail(Text.T("패키징할 index.html이 없습니다. JSON이나 CSS만으로는 EXE를 만들지 않습니다.",
                "No index.html to package. JSON or CSS alone is not turned into an EXE."), Joined(), Elapsed(started), BuildStage.ConfiguringBuild);

        if (request.Options.Method == BuildMethod.Electron)
            return await ElectronAsync(request, workspace, content, progress, log, started, cancellationToken);

        return await WebViewAsync(request, sdk.ExecutablePath!, content, index, log, started, Line, cancellationToken);
    }

    private async Task<BuildResult> WebViewAsync(BuildRequest request, string dotnet, string content, string index, List<string> log, DateTimeOffset started, Action<string> line, CancellationToken cancellationToken)
    {
        var name = SecurityManager.SanitizeFileName(request.Options.OutputName);
        var host = Path.Combine(Path.GetDirectoryName(content)!, "dex-webview-host");
        var www = Path.Combine(host, "wwwroot");
        Directory.CreateDirectory(www);
        foreach (var file in Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(content, file);
            var target = Path.Combine(www, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
        var relativeIndex = Path.GetRelativePath(content, index).Replace('\\', '/');
        var csproj = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <AssemblyName>HOSTNAME</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Web.WebView2" Version="1.0.2903.40" />
    <Content Include="wwwroot\**" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />
  </ItemGroup>
</Project>
""".Replace("HOSTNAME", name);
        var program = $$"""
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

var window = new Window { Title = "{{name}}", Width = 1100, Height = 760, WindowStartupLocation = WindowStartupLocation.CenterScreen };
var browser = new WebView2();
window.Content = browser;
window.Loaded += async (_, _) =>
{
    await browser.EnsureCoreWebView2Async();
    var index = Path.Combine(AppContext.BaseDirectory, "wwwroot", "{{relativeIndex}}");
    browser.CoreWebView2.Navigate(new Uri(index).AbsoluteUri);
};
new Application().Run(window);
""";
        var outDir = Path.Combine(host, "publish");
        line(Text.T("WebView2 호스트를 게시합니다. 최종 PC에는 WebView2 Runtime이 필요합니다.", "Publishing a WebView2 host. The target PC needs the WebView2 Runtime."));
        var result = await BuildWorkspace.PublishGeneratedAsync(_runner, dotnet, host, outDir, csproj, program, line, cancellationToken);
        if (result.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("WebView2 호스트 게시가 실패했습니다.", "Publishing the WebView2 host failed."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Packaging);
        var exe = Path.Combine(outDir, name + ".exe");
        var warnings = new List<string>
        {
            Text.T("이 EXE만 복사하면 wwwroot가 빠져 실행되지 않습니다. 출력 폴더 전체를 배포하세요.", "Copying only the EXE omits wwwroot. Distribute the whole output folder."),
            Text.T("Electron과 달리 Chromium을 포함하지 않습니다.", "Unlike Electron, Chromium is not bundled.")
        };
        return _verifier.Verify(exe, outDir, OutputKind.ApplicationFolder, string.Join(Environment.NewLine, log), Elapsed(started), warnings,
            Text.T("WebView2 애플리케이션 폴더", "WebView2 application folder"));
    }

    private async Task<BuildResult> ElectronAsync(BuildRequest request, string workspace, string content, IProgress<BuildProgress> progress, List<string> log, DateTimeOffset started, CancellationToken cancellationToken)
    {
        if (!request.Tools.TryGetValue("node", out var node) || node.ExecutablePath is null)
            return BuildVerifier.Fail(Text.T("Electron 패키징에는 Node.js가 필요합니다. 최종 사용자 PC에는 필요하지 않습니다.",
                "Electron packaging needs Node.js. End-user PCs do not."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.CheckingRequirements);
        progress.Report(new BuildProgress { Stage = BuildStage.Packaging, Message = "Electron" });
        log.Add(Text.T("Electron 패키징은 프로젝트에 electron-builder가 설치되어 있을 때만 실행합니다. DEX가 Electron 설치 파일을 받지 않습니다.",
            "Electron packaging runs only when electron-builder is already installed in the project. DEX does not download an Electron installer."));
        var builder = Path.Combine(workspace, "node_modules", "electron-builder", "cli.js");
        if (!File.Exists(builder))
            return BuildVerifier.Fail(Text.T("electron-builder가 프로젝트에 없습니다. 공식 Node.js 환경에서 개발 의존성으로 설치한 뒤 재검사하세요.",
                "electron-builder is not in the project. Install it as a dev dependency with the official Node.js toolchain, then recheck."),
                string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Packaging);
        var result = await _runner.RunAsync(node.ExecutablePath, [builder, "--win"], workspace, null, SecurityManager.BuildTimeout, 2_000_000, line => log.Add(line), false, cancellationToken);
        if (result.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("electron-builder가 실패했습니다.", "electron-builder failed."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Packaging);
        var exe = Directory.EnumerateFiles(Path.Combine(workspace, "dist"), "*.exe", SearchOption.AllDirectories).FirstOrDefault();
        return _verifier.Verify(exe, Path.Combine(workspace, "dist"), OutputKind.Installer, string.Join(Environment.NewLine, log), Elapsed(started),
            [Text.T("Electron 출력은 Chromium과 Node 런타임을 포함합니다.", "Electron output includes Chromium and the Node runtime.")], "Electron");
    }

    private static bool HasScript(string packageJson, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(packageJson));
            return doc.RootElement.TryGetProperty("scripts", out var scripts) && scripts.TryGetProperty(name, out _);
        }
        catch { return false; }
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
