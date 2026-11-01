using DEX.Core.Localization;
using DEX.Core.Models;

namespace DEX.Core.Compatibility;

public sealed class BuildConfigurationManager
{
    public OptionAvailability GetAvailability(ProjectAnalysis analysis)
    {
        var method = analysis.RecommendedMethod;
        var methods = new List<BuildMethod>();
        if (method != BuildMethod.None)
            methods.Add(method);
        methods.AddRange(analysis.AlternativeMethods);
        var web = analysis.Kind == ProjectKind.Web;
        var dotnet = analysis.Kind == ProjectKind.DotNet;
        var python = analysis.Kind == ProjectKind.Python && method != BuildMethod.None;
        var native = analysis.Kind is ProjectKind.Cpp or ProjectKind.Go or ProjectKind.Rust;
        return new OptionAvailability
        {
            Icon = python || dotnet || web,
            ProductName = dotnet || web || analysis.Kind == ProjectKind.Java,
            CompanyName = dotnet || web,
            ProductVersion = dotnet || python || analysis.Kind == ProjectKind.Java,
            Console = python || dotnet,
            SingleFile = python || dotnet || analysis.Kind == ProjectKind.Go,
            SelfContained = dotnet,
            IncludeDependencies = python || web || analysis.Kind is ProjectKind.Java or ProjectKind.Go or ProjectKind.Rust or ProjectKind.Php,
            Optimize = dotnet || native || analysis.Kind == ProjectKind.Java || analysis.Kind == ProjectKind.Rust,
            Compression = python || dotnet,
            Architectures = analysis.Kind switch
            {
                ProjectKind.DotNet or ProjectKind.Go or ProjectKind.Rust => [ArchitectureKind.X64, ArchitectureKind.X86, ArchitectureKind.Arm64],
                ProjectKind.Cpp => [ArchitectureKind.X64, ArchitectureKind.X86, ArchitectureKind.Arm64],
                _ => [ArchitectureKind.X64]
            },
            Methods = methods,
            MethodExplanation = Explain(method)
        };
    }

    public static string Explain(BuildMethod method) => method switch
    {
        BuildMethod.WebView2 => Text.T(
            "WebView2는 설치된 Edge 런타임으로 HTML을 표시합니다. 결과물이 작고, 최종 PC에 WebView2 Runtime이 필요합니다. Windows 11에는 보통 포함되어 있습니다.",
            "WebView2 shows HTML with the installed Edge runtime. Output is smaller, and the target PC needs the WebView2 Runtime. Windows 11 usually includes it."),
        BuildMethod.Electron => Text.T(
            "Electron은 Chromium과 Node.js를 패키지에 포함합니다. 용량이 크지만 최종 사용자에게 Node.js 설치가 필수는 아닙니다.",
            "Electron bundles Chromium and Node.js. The package is larger, but end users do not need Node.js installed."),
        BuildMethod.PyInstaller => Text.T("PyInstaller는 Python 인터프리터와 의존성을 EXE로 묶습니다. 소스를 기계어로 컴파일하지는 않습니다.",
            "PyInstaller bundles the Python interpreter and dependencies. It does not compile source to machine code."),
        BuildMethod.Nuitka => Text.T("Nuitka는 C 컴파일러로 컴파일합니다. C/C++ 빌드 환경이 없으면 사용할 수 없습니다.",
            "Nuitka compiles with a C compiler. It cannot be used without a C/C++ build environment."),
        BuildMethod.ScriptWrapper => Text.T("이 출력은 스크립트를 실행하는 래퍼입니다. 네이티브 컴파일이 아닙니다.",
            "This output is a wrapper that launches the script. It is not a native compile."),
        BuildMethod.PhpHost or BuildMethod.RubyHost => Text.T("런타임 폴더를 포함하는 호스트입니다. 언어를 네이티브 EXE로 컴파일한다고 표시하지 않습니다.",
            "This is a host that includes a runtime folder. It is not described as a native compile."),
        BuildMethod.Jpackage => Text.T("jpackage --type exe는 설치 프로그램입니다. WiX가 없으면 애플리케이션 이미지로 만들고 그 안의 EXE를 구분해 보고합니다.",
            "jpackage --type exe is an installer. Without WiX, DEX builds an application image and reports the EXE inside it separately."),
        _ => Text.Method(method)
    };
}
