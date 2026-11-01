using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class JavaBuilder
{
    private readonly ProcessRunner _runner;
    private readonly BuildVerifier _verifier;

    public JavaBuilder(ProcessRunner runner, BuildVerifier verifier)
    {
        _runner = runner;
        _verifier = verifier;
    }

    public async Task<BuildResult> BuildAsync(BuildRequest request, string workspace, IProgress<BuildProgress> progress, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var log = new List<string>();
        void Line(string text) { log.Add(text); }
        if (!request.Tools.TryGetValue("jdk", out var jdk) || jdk.ExecutablePath is null)
            return BuildVerifier.Fail(Text.T("JDK가 없습니다. JRE만으로는 패키징하지 않습니다.", "No JDK. A JRE alone is not used for packaging."), "", Elapsed(started), BuildStage.CheckingRequirements);
        var jpackage = Path.Combine(Path.GetDirectoryName(jdk.ExecutablePath)!, "jpackage.exe");
        var jarTool = Path.Combine(Path.GetDirectoryName(jdk.ExecutablePath)!, "jar.exe");
        if (!File.Exists(jpackage))
            return BuildVerifier.Fail(Text.T("jpackage.exe가 JDK에 없습니다.", "jpackage.exe is not in the JDK."), "", Elapsed(started), BuildStage.CheckingRequirements);

        progress.Report(new BuildProgress { Stage = BuildStage.Compiling, Message = Text.Stage(BuildStage.Compiling) });
        var mvnw = Path.Combine(workspace, "mvnw.cmd");
        var gradlew = Path.Combine(workspace, "gradlew.bat");
        ProcessResult? build = null;
        if (File.Exists(mvnw))
            build = await _runner.RunBatchAsync(mvnw, ["-DskipTests", "package"], workspace, SecurityManager.BuildTimeout, Line, cancellationToken);
        else if (File.Exists(gradlew))
            build = await _runner.RunBatchAsync(gradlew, ["build", "-x", "test"], workspace, SecurityManager.BuildTimeout, Line, cancellationToken);
        else if (File.Exists(Path.Combine(workspace, "pom.xml")) && request.Tools.TryGetValue("maven", out var maven) && maven.ExecutablePath is not null)
            build = await _runner.RunBatchAsync(maven.ExecutablePath, ["-DskipTests", "package"], workspace, SecurityManager.BuildTimeout, Line, cancellationToken);
        else if (Directory.EnumerateFiles(workspace, "*.java").Any())
        {
            var sources = Directory.EnumerateFiles(workspace, "*.java", SearchOption.AllDirectories).ToList();
            var classes = Path.Combine(workspace, "dex-classes");
            Directory.CreateDirectory(classes);
            var javac = Path.Combine(Path.GetDirectoryName(jdk.ExecutablePath)!, "javac.exe");
            var javacArgs = new List<string> { "-d", classes };
                        javacArgs.AddRange(sources);
                        build = await _runner.RunAsync(javac, javacArgs, workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
            if (build.ExitCode == 0)
            {
                var builtJar = Path.Combine(workspace, "app.jar");
                build = await _runner.RunAsync(jarTool, ["cfe", builtJar, MainClass(request, workspace), "-C", classes, "."], workspace, null, TimeSpan.FromMinutes(5), 200_000, Line, false, cancellationToken);
            }
        }

        if (build is not null && build.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("Java 빌드가 실패했습니다.", "The Java build failed."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Compiling);

        var jar = Directory.EnumerateFiles(workspace, "*.jar", SearchOption.AllDirectories)
            .FirstOrDefault(f => !f.Contains("sources", StringComparison.OrdinalIgnoreCase) && !f.Contains("javadoc", StringComparison.OrdinalIgnoreCase));
        if (jar is null)
            return BuildVerifier.Fail(Text.T("패키징할 JAR가 없습니다. JAR 이름을 EXE로 바꾸지 않습니다.",
                "No JAR to package. A JAR is not renamed to EXE."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Packaging);

        var name = SecurityManager.SanitizeFileName(request.Options.OutputName);
        var dest = Path.Combine(workspace, "dex-jpackage");
        Directory.CreateDirectory(dest);
        var input = Path.Combine(workspace, "dex-jars");
        Directory.CreateDirectory(input);
        File.Copy(jar, Path.Combine(input, Path.GetFileName(jar)), true);
        var packageType = "app-image";
        var description = Text.T("jpackage 애플리케이션 이미지입니다. 설치 프로그램 EXE가 아닙니다.", "This is a jpackage application image, not an installer EXE.");
        progress.Report(new BuildProgress { Stage = BuildStage.Packaging, Message = description });
        var args = new List<string>
        {
            "--type", packageType,
            "--dest", dest,
            "--name", name,
            "--input", input,
            "--main-jar", Path.GetFileName(jar)
        };
        var main = MainClass(request, workspace);
        if (!string.IsNullOrWhiteSpace(main))
        {
            args.Add("--main-class");
            args.Add(main);
        }
        var packaged = await _runner.RunAsync(jpackage, args, workspace, null, SecurityManager.BuildTimeout, 2_000_000, Line, false, cancellationToken);
        if (packaged.ExitCode != 0)
            return BuildVerifier.Fail(Text.T("jpackage가 실패했습니다. 설치 프로그램용 WiX가 없어도 이미지 생성이 실패하면 성공으로 표시하지 않습니다.",
                "jpackage failed. A missing WiX installer toolchain is not reported as success."), string.Join(Environment.NewLine, log), Elapsed(started), BuildStage.Packaging);
        var exe = Directory.EnumerateFiles(dest, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
        return _verifier.Verify(exe, dest, OutputKind.ApplicationFolder, string.Join(Environment.NewLine, log) + Environment.NewLine + description, Elapsed(started), [description], description);
    }

    private static string MainClass(BuildRequest request, string workspace)
    {
        var entry = request.Options.SelectedEntry ?? request.Analysis.EntryPoint;
        if (string.IsNullOrWhiteSpace(entry) || !entry.EndsWith(".java", StringComparison.OrdinalIgnoreCase))
            return "";
        var text = File.ReadAllText(Path.Combine(workspace, entry));
        var pkg = System.Text.RegularExpressions.Regex.Match(text, @"package\s+([A-Za-z0-9_.]+)\s*;");
        var cls = System.Text.RegularExpressions.Regex.Match(text, @"class\s+([A-Za-z0-9_]+)");
        if (!cls.Success)
            return "";
        return pkg.Success ? pkg.Groups[1].Value + "." + cls.Groups[1].Value : cls.Groups[1].Value;
    }

    private static TimeSpan Elapsed(DateTimeOffset started) => DateTimeOffset.UtcNow - started;
}
