using DEX.Core.Localization;
using DEX.Core.Models;

namespace DEX.Core.Analyzers;

public sealed class ProjectAnalyzer
{
    private readonly ProjectScanner _scanner = new();
    private readonly LanguageDetector _languages = new();
    private readonly FrameworkDetector _frameworks = new();
    private readonly EntryPointDetector _entries = new();
    private readonly DependencyAnalyzer _dependencies = new();

    public ProjectAnalysis Analyze(string root, bool fromArchive, string? displayName = null)
    {
        var (files, truncated, warnings) = _scanner.Scan(root);
        var hits = _languages.Detect(files);
        var primary = hits.FirstOrDefault();
        var kind = primary?.Kind ?? ProjectKind.Unknown;
        var ambiguous = hits.Count > 1 && hits[1].Score >= 80 && hits[0].Score - hits[1].Score <= 15;
        var (framework, packageManager, wrapper, tfm, requiresMsvc, windowed) = _frameworks.Detect(root, kind, files);
        var (entry, candidates, mustChoose) = _entries.Detect(root, kind, files);
        var deps = _dependencies.Analyze(kind, files);
        var notes = new List<string>();
        var warningList = warnings.ToList();
        var support = SupportLevel.Full;
        var method = BuildMethod.None;
        var alternatives = new List<BuildMethod>();
        var output = "";
        string? requiredVersion = null;

        if (kind == ProjectKind.Python)
        {
            requiredVersion = _dependencies.PythonRequirement(files);
            method = BuildMethod.PyInstaller;
            alternatives.Add(BuildMethod.Nuitka);
            output = Text.T("PyInstaller 단일 EXE 패키지. 네이티브 컴파일이 아닙니다.", "PyInstaller single-file package. This is not a native compile.");
            if (files.Any(f => f.Extension == ".pyc") && !files.Any(f => f.Extension is ".py" or ".pyw"))
            {
                support = SupportLevel.Conditional;
                method = BuildMethod.None;
                notes.Add(Text.T(".pyc는 바이트코드입니다. 원본 소스 없이 EXE로 변환하지 않습니다. 헤더로 버전만 확인합니다.",
                    ".pyc is bytecode. DEX does not convert it to an EXE without source. Only the header version is checked."));
                var pyc = files.First(f => f.Extension == ".pyc");
                notes.Add(PycNote(pyc.FullPath));
            }
        }
        else if (kind == ProjectKind.Web)
        {
            requiredVersion = _dependencies.NodeRequirement(files);
            var electron = string.Equals(framework, "Electron", StringComparison.OrdinalIgnoreCase);
            method = electron ? BuildMethod.Electron : BuildMethod.WebView2;
            alternatives.Add(electron ? BuildMethod.WebView2 : BuildMethod.Electron);
            output = method == BuildMethod.WebView2
                ? Text.T("WebView2 호스트 EXE. 최종 PC에 WebView2 Runtime이 필요합니다. Node.js는 최종 사용자에게 필요하지 않습니다.",
                    "WebView2 host EXE. The target PC needs the WebView2 Runtime. End users do not need Node.js.")
                : Text.T("Electron 패키지. Chromium과 Node 런타임을 포함하므로 최종 사용자에게 Node.js 설치를 요구하지 않습니다.",
                    "Electron package. Chromium and Node are bundled, so end users are not asked to install Node.js.");
            if (framework is "React" or "Vue" or "Svelte" or "Next.js" or "TypeScript")
            {
                support = SupportLevel.Conditional;
                notes.Add(Text.T("웹 프로젝트를 먼저 빌드한 뒤 데스크톱 호스트로 패키징합니다.", "The web project is built first, then packaged in a desktop host."));
            }
            if (files.Any(f => f.Extension == ".css") && !files.Any(f => f.Extension is ".html" or ".htm") && framework is null)
            {
                kind = ProjectKind.Resource;
                support = SupportLevel.ResourceOnly;
                method = BuildMethod.None;
            }
            if (!files.Any(f => f.Extension is ".html" or ".htm") && !files.Any(f => Path.GetFileName(f.RelativePath).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
            {
                support = SupportLevel.Conditional;
                notes.Add(Text.T("JavaScript 단독 파일은 실행 환경과 진입점이 있어야 합니다. UI 진입점이 없으면 데스크톱 EXE로 만들지 않습니다.",
                    "A standalone JavaScript file needs a runtime and an entry point. Without a UI entry, DEX does not produce a desktop EXE."));
                if (entry is null)
                    method = BuildMethod.None;
            }
        }
        else if (kind == ProjectKind.DotNet)
        {
            method = BuildMethod.DotNetPublish;
            output = Text.T("dotnet publish. self-contained 단일 파일이면 최종 PC에 같은 런타임이 없어도 됩니다. framework-dependent이면 최종 PC에 런타임이 필요합니다.",
                "dotnet publish. A self-contained single file does not require the same runtime on the target PC. Framework-dependent output does.");
            if (tfm?.Contains("net4", StringComparison.OrdinalIgnoreCase) == true || tfm?.StartsWith("v4", StringComparison.OrdinalIgnoreCase) == true)
            {
                support = SupportLevel.Conditional;
                notes.Add(Text.T(".NET Framework 프로젝트는 최신 .NET SDK 프로젝트와 구분합니다. Developer Pack이 필요할 수 있습니다.",
                    ".NET Framework projects are distinct from modern SDK projects. A Developer Pack may be required."));
            }
            requiredVersion = SdkMajor(tfm);
        }
        else if (kind == ProjectKind.Cpp)
        {
            method = requiresMsvc || files.Any(f => f.Extension == ".vcxproj")
                ? BuildMethod.MsBuild
                : files.Any(f => f.RelativePath.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)) ? BuildMethod.CMake : BuildMethod.MsBuild;
            if (!requiresMsvc && files.Any(f => f.RelativePath.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)))
                alternatives.Add(BuildMethod.MinGw);
            output = Text.T("네이티브 C/C++ 컴파일. MSVC가 필요한 프로젝트는 MinGW로 바꾸지 않습니다.",
                "Native C/C++ compile. Projects that require MSVC are not switched to MinGW.");
            support = files.Any(f => f.Extension == ".vcxproj" || f.RelativePath.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase))
                ? SupportLevel.Full
                : SupportLevel.Conditional;
        }
        else if (kind == ProjectKind.Java)
        {
            method = BuildMethod.Jpackage;
            output = Text.T("jpackage 출력은 설치용 EXE와 애플리케이션 이미지를 구분합니다. JAR 확장자만 바꾸지 않습니다.",
                "jpackage output is reported as an installer EXE or an application image. A JAR is never renamed to EXE.");
            support = files.Any(f => f.Extension == ".java" || f.RelativePath.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase) || f.RelativePath.Contains("gradle", StringComparison.OrdinalIgnoreCase))
                ? SupportLevel.Full
                : SupportLevel.Conditional;
        }
        else if (kind == ProjectKind.Go)
        {
            method = BuildMethod.GoBuild;
            requiredVersion = _dependencies.GoRequirement(files);
            output = Text.T("go build Windows 대상 네이티브 EXE.", "go build native Windows EXE.");
        }
        else if (kind == ProjectKind.Rust)
        {
            method = BuildMethod.Cargo;
            output = Text.T("cargo build --release. Windows 링커가 필요합니다.", "cargo build --release. A Windows linker is required.");
        }
        else if (kind == ProjectKind.Php)
        {
            method = BuildMethod.PhpHost;
            support = SupportLevel.Experimental;
            output = Text.T("PHP를 네이티브 EXE로 컴파일하지 않습니다. PHP 런타임을 포함하는 호스트만 생성합니다.",
                "PHP is not compiled to a native EXE. Only a host that includes the PHP runtime is produced.");
        }
        else if (kind == ProjectKind.Ruby)
        {
            method = BuildMethod.RubyHost;
            support = SupportLevel.Experimental;
            output = Text.T("검증된 Ruby 네이티브 패키징 백엔드가 없어 실험적 런타임 호스트만 제공합니다.",
                "No verified native Ruby packager is available. Only an experimental runtime host is offered.");
        }
        else if (kind == ProjectKind.Script)
        {
            method = BuildMethod.ScriptWrapper;
            support = SupportLevel.Conditional;
            output = Text.T("네이티브 컴파일이 아니라 스크립트 실행 래퍼입니다.", "This is an execution wrapper, not a native compile.");
        }
        else if (kind == ProjectKind.Swift)
        {
            support = SupportLevel.NotSupported;
            method = BuildMethod.None;
            notes.Add(Text.T("이 PC에서 사용 가능한 Swift Windows 컴파일러를 확인하지 않으면 지원하지 않습니다.",
                "Swift is unsupported unless a usable Windows compiler is detected, which DEX does not bundle."));
        }
        else if (kind == ProjectKind.Resource)
        {
            support = SupportLevel.ResourceOnly;
            method = BuildMethod.None;
            output = Text.T("독립 실행 프로그램이 아닌 리소스입니다.", "This is a resource, not a standalone program.");
            notes.Add(Text.T("JSON, CSS, 이미지 등은 단독 EXE로 변환하지 않습니다.", "JSON, CSS, images, and similar files are not converted into a standalone EXE."));
        }
        else
        {
            support = SupportLevel.NotSupported;
            notes.Add(Text.T("지원되는 프로젝트 구조를 찾지 못했습니다. 추측으로 변환하지 않습니다.",
                "No supported project structure was found. DEX will not guess a conversion."));
        }

        if (ambiguous)
        {
            notes.Add(Text.T("여러 언어 신호가 비슷합니다. 프로젝트를 하나 선택해야 합니다.", "Several language signals are equally strong. Choose one project."));
            mustChoose = true;
        }

        var requirements = BuildRequirements(kind, method, packageManager, wrapper, tfm, requiresMsvc, files);
        var projectFile = candidates.FirstOrDefault()?.RelativePath;

        return new ProjectAnalysis
        {
            ProjectName = displayName ?? new DirectoryInfo(root).Name,
            RootPath = root,
            FromArchive = fromArchive,
            Kind = kind,
            LanguageName = LanguageName(kind),
            Framework = framework,
            SupportLevel = support,
            StructureSummary = $"{files.Count} files; " + string.Join(", ", hits.Take(3).Select(h => h.Kind + ":" + h.Evidence)),
            EntryPoint = mustChoose ? null : entry,
            EntryCandidates = candidates,
            EntryPointRequiredFromUser = mustChoose || (entry is null && method != BuildMethod.None),
            Requirements = requirements,
            Dependencies = deps,
            RecommendedMethod = method,
            AlternativeMethods = alternatives,
            OutputType = output,
            RequiredVersionText = requiredVersion,
            Notes = notes,
            Warnings = warningList,
            AnalysisTruncated = truncated,
            IsAmbiguous = ambiguous,
            AmbiguousKinds = ambiguous ? hits.Take(3).Select(h => h.Kind).ToList() : [],
            ProjectFile = projectFile,
            TargetFramework = tfm,
            RequiresMsvc = requiresMsvc,
            HasPackageManagerWrapper = wrapper,
            PackageManagerId = packageManager,
            WindowedHint = windowed,
            SecondaryLanguages = hits.Skip(1).Take(3).Select(h => h.Kind.ToString()).ToList()
        };
    }

    private static List<Requirement> BuildRequirements(
        ProjectKind kind, BuildMethod method, string? packageManager, bool wrapper, string? tfm, bool requiresMsvc, IReadOnlyList<ScannedFile> files)
    {
        var list = new List<Requirement>();
        void Need(string id, string reason, string? version = null, bool required = true) =>
            list.Add(new Requirement { ToolId = id, IsRequired = required, Reason = reason, RequiredVersion = version });

        switch (kind)
        {
            case ProjectKind.Python when method != BuildMethod.None:
                Need("python", Text.T("소스 패키징에 Python과 pip가 필요합니다.", "Python and pip are required to package the source."), null);
                break;
            case ProjectKind.Web:
                if (files.Any(f => Path.GetFileName(f.RelativePath).Equals("package.json", StringComparison.OrdinalIgnoreCase)))
                {
                    Need("node", Text.T("웹 프로젝트를 빌드합니다.", "Builds the web project."));
                    Need(packageManager ?? "npm", Text.T("의존성과 빌드 스크립트를 실행합니다.", "Runs dependencies and the build script."));
                }
                Need("dotnet-sdk", Text.T("WebView2 또는 호스트 EXE를 컴파일합니다.", "Compiles the WebView2 or host EXE."));
                if (method == BuildMethod.WebView2)
                    Need("webview2", Text.T("이 PC에서 결과 확인과 최종 실행에 필요합니다.", "Needed to verify here and to run on the target PC."), required: false);
                break;
            case ProjectKind.DotNet:
                if (tfm?.Contains('4') == true && (tfm.StartsWith("v4", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("net4", StringComparison.OrdinalIgnoreCase)))
                    Need("dotnet-framework", Text.T("Targeting Pack이 필요합니다.", "A targeting pack is required."), tfm.TrimStart('v', 'V'));
                else
                    Need("dotnet-sdk", Text.T("빌드에는 Runtime이 아니라 SDK가 필요합니다.", "Building needs the SDK, not only the runtime."), SdkMajor(tfm));
                break;
            case ProjectKind.Cpp:
                if (requiresMsvc || method == BuildMethod.MsBuild)
                    Need("vs-buildtools", Text.T("Desktop development with C++, MSVC, Windows SDK, MSBuild. 전체 IDE는 필수가 아닙니다.",
                        "Desktop development with C++, MSVC, Windows SDK, MSBuild. The full IDE is not required."));
                if (method == BuildMethod.CMake)
                    Need("cmake", Text.T("CMake 구성에 필요합니다.", "Required to configure CMake."));
                if (method == BuildMethod.MinGw)
                    Need("mingw", Text.T("프로젝트가 GCC/MinGW를 사용할 때만 필요합니다.", "Required only when the project uses GCC/MinGW."));
                break;
            case ProjectKind.Java:
                Need("jdk", Text.T("컴파일과 jpackage에는 JDK가 필요합니다. JRE만으로는 부족합니다.", "Compile and jpackage need a JDK. A JRE is not enough."));
                if (!wrapper && packageManager == "maven")
                    Need("maven", Text.T("Maven Wrapper가 없으므로 Maven이 필요합니다.", "Maven is required because the Maven Wrapper is absent."));
                if (!wrapper && packageManager == "gradle")
                    Need("gradle", Text.T("Gradle Wrapper가 없으므로 Gradle이 필요합니다.", "Gradle is required because the Gradle Wrapper is absent."));
                break;
            case ProjectKind.Go:
                Need("go", Text.T("go build에 Go SDK가 필요합니다.", "go build requires the Go SDK."));
                break;
            case ProjectKind.Rust:
                Need("rust", Text.T("rustc와 cargo가 필요합니다.", "rustc and cargo are required."));
                Need("vs-buildtools", Text.T("MSVC 타깃의 링커로 사용합니다.", "Used as the linker for the MSVC target."));
                break;
            case ProjectKind.Php:
                Need("php", Text.T("포함할 PHP 런타임을 찾기 위해 필요합니다.", "Required so the PHP runtime can be copied into the host."));
                Need("dotnet-sdk", Text.T("호스트 EXE를 컴파일합니다.", "Compiles the host EXE."));
                break;
            case ProjectKind.Ruby:
                Need("ruby", Text.T("실험적 호스트에 포함할 런타임입니다.", "Runtime copied into the experimental host."));
                Need("dotnet-sdk", Text.T("호스트 EXE를 컴파일합니다.", "Compiles the host EXE."));
                break;
            case ProjectKind.Script:
                Need("dotnet-sdk", Text.T("래퍼 EXE를 컴파일합니다. 스크립트 자체를 네이티브 컴파일하지는 않습니다.",
                    "Compiles the wrapper EXE. The script itself is not native-compiled."));
                break;
        }

        if (files.Any(f => f.RelativePath.Replace('\\', '/').Contains(".gitmodules", StringComparison.OrdinalIgnoreCase)))
            Need("git", Text.T("서브모듈이 있습니다.", "Submodules are present."));
        return list;
    }

    private static string? SdkMajor(string? tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(tfm, @"net(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string PycNote(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var magic = stream.ReadByte() | (stream.ReadByte() << 8);
            var version = magic switch
            {
                3495 => "3.11",
                3531 => "3.12",
                3571 => "3.13",
                3413 => "3.10",
                3425 => "3.10",
                _ => Text.T("알 수 없음", "unknown")
            };
            return Text.T($"pyc magic {magic}, 추정 Python {version}.", $"pyc magic {magic}, estimated Python {version}.");
        }
        catch
        {
            return Text.T("pyc 헤더를 읽지 못했습니다.", "Could not read the pyc header.");
        }
    }

    private static string LanguageName(ProjectKind kind) => kind switch
    {
        ProjectKind.Python => "Python",
        ProjectKind.Web => "HTML/CSS/JavaScript",
        ProjectKind.DotNet => "C# / .NET",
        ProjectKind.Cpp => "C / C++",
        ProjectKind.Java => "Java",
        ProjectKind.Go => "Go",
        ProjectKind.Rust => "Rust",
        ProjectKind.Php => "PHP",
        ProjectKind.Ruby => "Ruby",
        ProjectKind.Script => "Windows Script",
        ProjectKind.Swift => "Swift",
        ProjectKind.Resource => Text.T("리소스", "Resource"),
        _ => Text.T("알 수 없음", "Unknown")
    };
}
