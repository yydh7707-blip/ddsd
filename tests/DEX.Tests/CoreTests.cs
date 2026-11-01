using System.IO.Compression;
using DEX.Core.Analyzers;
using DEX.Core.Builders;
using DEX.Core.Compatibility;
using DEX.Core.Installation;
using DEX.Core.Models;
using DEX.Core.Probes;
using DEX.Core.Security;
using DEX.Core.Services;

namespace DEX.Tests;

public class CoreTests
{
    [Fact]
    public void Detects_python_react_csharp_cpp_java_and_rust()
    {
        var analyzer = new ProjectAnalyzer();
        Assert.Equal(ProjectKind.Python, analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "app.py"), "print(1)\n")), false).Kind);
        Assert.Equal("React", analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "package.json"), """{"dependencies":{"react":"18.0.0"}}""")), false).Framework);
        Assert.Equal(ProjectKind.DotNet, analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "App.csproj"), "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>")), false).Kind);
        var cpp = analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "App.vcxproj"), "<Project/>")), false);
        Assert.Equal(ProjectKind.Cpp, cpp.Kind);
        Assert.DoesNotContain(cpp.Requirements, r => r.ToolId == "mingw");
        Assert.Equal(ProjectKind.Java, analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "pom.xml"), "<project></project>")), false).Kind);
        Assert.Equal(ProjectKind.Rust, analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "Cargo.toml"), "[package]\nname=\"a\"\nversion=\"0.1.0\"")), false).Kind);
    }

    [Fact]
    public void Html_is_conditional_and_css_or_json_is_not_an_exe()
    {
        var analyzer = new ProjectAnalyzer();
        var html = analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "index.html"), "<html></html>")), false);
        Assert.Equal(BuildMethod.WebView2, html.RecommendedMethod);
        Assert.Contains("WebView2", html.OutputType, StringComparison.OrdinalIgnoreCase);
        var css = analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "site.css"), "body{}")), false);
        Assert.Equal(SupportLevel.ResourceOnly, css.SupportLevel);
        var json = analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "data.json"), "{}")), false);
        Assert.Equal(SupportLevel.ResourceOnly, json.SupportLevel);
    }

    [Fact]
    public void Missing_entry_and_version_mismatch_block_the_build()
    {
        var analyzer = new ProjectAnalyzer();
        var engine = new CompatibilityEngine();
        var many = analyzer.Analyze(Fixture(d =>
        {
            File.WriteAllText(Path.Combine(d, "a.py"), "x = 1");
            File.WriteAllText(Path.Combine(d, "b.py"), "y = 2");
        }), false);
        var report = engine.Evaluate(many, [Installed("python")], null);
        Assert.Equal(CompatibilityStatus.NeedsConfiguration, report.Status);
        Assert.False(report.BuildAllowed);

        var one = analyzer.Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "app.py"), "print(1)")), false);
        var mismatch = engine.Evaluate(one, [new ToolStatus { ToolId = "python", Name = "Python", State = InstallState.VersionMismatch, Version = "3.9" }], "app.py");
        Assert.Equal(CompatibilityStatus.VersionMismatch, mismatch.Status);
        Assert.False(mismatch.BuildAllowed);

        var missing = engine.Evaluate(one, [new ToolStatus { ToolId = "python", Name = "Python", State = InstallState.NotInstalled }], "app.py");
        Assert.Equal(CompatibilityStatus.NeedsSetup, missing.Status);
        var ready = engine.Evaluate(one, [Installed("python")], "app.py");
        Assert.True(ready.BuildAllowed);
    }

    [Fact]
    public void Pyc_only_is_not_converted()
    {
        var dir = Fixture(d =>
        {
            using var stream = File.Create(Path.Combine(d, "app.pyc"));
            stream.WriteByte(0xA7);
            stream.WriteByte(0x0D);
        });
        var analysis = new ProjectAnalyzer().Analyze(dir, false);
        Assert.Equal(BuildMethod.None, analysis.RecommendedMethod);
        Assert.Contains(analysis.Notes, n => n.Contains("pyc", StringComparison.OrdinalIgnoreCase) || n.Contains("magic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Zip_slip_is_rejected_and_valid_zip_extracts()
    {
        var zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("../evil.txt");
        var extractor = new ZipExtractor();
        Assert.Throws<InvalidOperationException>(() => extractor.Extract(zip, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        var good = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        var dest = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using (var archive = ZipFile.Open(good, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("src/app.py");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("print(1)");
        }
        extractor.Extract(good, dest);
        Assert.True(File.Exists(Path.Combine(dest, "src", "app.py")));
    }

    [Fact]
    public void Official_urls_are_https_allowlisted()
    {
        Assert.True(UrlGuard.IsAllowed("https://www.python.org/downloads/windows/"));
        Assert.True(UrlGuard.IsAllowed("https://dotnet.microsoft.com/download/dotnet"));
        Assert.False(UrlGuard.IsAllowed("http://www.python.org/downloads/windows/"));
        Assert.False(UrlGuard.IsAllowed("https://evil.example/python"));
        Assert.False(UrlGuard.IsAllowed("https://bit.ly/python"));
        Assert.False(UrlGuard.IsAllowed("https://aka.ms/dotnet"));
        Assert.All(OfficialToolCatalog.All.Select(t => t.DownloadUrl), url => Assert.True(UrlGuard.IsAllowed(url)));
    }

    [Fact]
    public void Version_parser_compares_requirements()
    {
        Assert.True(VersionParser.Satisfies("8.0.427", "8"));
        Assert.True(VersionParser.Satisfies("3.12.1", ">=3.11"));
        Assert.False(VersionParser.Satisfies("3.9.0", ">=3.11"));
    }

    [Fact]
    public async Task Installed_dotnet_is_detected()
    {
        var service = new EnvironmentDetectionService(new ProcessRunner(), new ToolLocator());
        var status = await service.CheckAsync("dotnet-sdk", "8", CancellationToken.None);
        Assert.Equal(InstallState.Installed, status.State);
        Assert.StartsWith("8", status.Version);
        var missing = await service.CheckAsync("not-a-tool", null, CancellationToken.None);
        Assert.Equal(InstallState.NotInstalled, missing.State);
    }

    [Fact]
    public async Task Build_without_consent_or_project_fails()
    {
        var manager = new BuildManager(new ProcessRunner(), new BuildVerifier(), new ToolLocator());
        var analysis = new ProjectAnalyzer().Analyze(Fixture(d => File.WriteAllText(Path.Combine(d, "app.py"), "print(1)")), false);
        var denied = await manager.BuildAsync(new BuildRequest
        {
            ProjectRoot = analysis.RootPath,
            Analysis = analysis,
            Options = new BuildOptions(),
            Tools = new Dictionary<string, ToolStatus>(),
            ConsentGranted = false
        }, null, CancellationToken.None);
        Assert.False(denied.Success);

        var builder = new DotNetBuilder(new ProcessRunner(), new BuildVerifier());
        var failed = await builder.BuildAsync(new BuildRequest
        {
            ProjectRoot = analysis.RootPath,
            Analysis = analysis,
            Options = new BuildOptions { Method = BuildMethod.DotNetPublish, OutputName = "Nope" },
            Tools = new Dictionary<string, ToolStatus> { ["dotnet-sdk"] = Installed("dotnet-sdk") },
            ConsentGranted = true
        }, analysis.RootPath, new Progress<BuildProgress>(), CancellationToken.None);
        Assert.False(failed.Success);
        Assert.DoesNotContain(Directory.EnumerateFiles(analysis.RootPath, "*.exe", SearchOption.AllDirectories), _ => true);
    }

    [Fact(Timeout = 180000)]
    public async Task DotNet_project_builds_a_real_exe()
    {
        var root = Fixture(d =>
        {
            File.WriteAllText(Path.Combine(d, "Hello.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(d, "Program.cs"), "Console.WriteLine(\"dex\");");
        });
        var analysis = new ProjectAnalyzer().Analyze(root, false, "Hello");
        var tools = await new EnvironmentDetectionService(new ProcessRunner(), new ToolLocator()).CheckAsync("dotnet-sdk", null, CancellationToken.None);
        var result = await new DotNetBuilder(new ProcessRunner(), new BuildVerifier()).BuildAsync(new BuildRequest
        {
            ProjectRoot = root,
            Analysis = analysis,
            Options = new BuildOptions
            {
                OutputName = "HelloDex",
                Method = BuildMethod.DotNetPublish,
                SelfContained = false,
                SingleFile = false,
                Optimize = true,
                Architecture = ArchitectureKind.X64,
                ProductVersion = "1.0.0"
            },
            Tools = new Dictionary<string, ToolStatus> { ["dotnet-sdk"] = tools },
            ConsentGranted = true
        }, root, new Progress<BuildProgress>(), CancellationToken.None);
        Assert.True(result.Success, result.Error + Environment.NewLine + result.Log);
        Assert.True(File.Exists(result.ExecutablePath));
        Assert.True(PeVerifier.Read(result.ExecutablePath!).IsPe);
    }

    private static ToolStatus Installed(string id) => new()
    {
        ToolId = id,
        Name = id,
        State = InstallState.Installed,
        Version = "9.9.9",
        ExecutablePath = "C:\\tool\\tool.exe"
    };

    private static string Fixture(Action<string> write)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        write(dir);
        return dir;
    }
}
