using DEX.Core.Models;

namespace DEX.Core.Localization;

public static class Text
{
    public static string Language { get; set; } = "en";

    public static string T(string korean, string english) =>
        string.Equals(Language, "en", StringComparison.OrdinalIgnoreCase) ? english : korean;

    public static string Status(CompatibilityStatus status) => status switch
    {
        CompatibilityStatus.Ready => T("빌드 준비 완료", "READY"),
        CompatibilityStatus.NeedsSetup => T("설치 필요", "NEEDS SETUP"),
        CompatibilityStatus.VersionMismatch => T("버전 불일치", "VERSION MISMATCH"),
        CompatibilityStatus.NeedsConfiguration => T("설정 필요", "NEEDS CONFIGURATION"),
        CompatibilityStatus.DependencyError => T("의존성 오류", "DEPENDENCY ERROR"),
        CompatibilityStatus.PartiallySupported => T("부분 지원", "PARTIALLY SUPPORTED"),
        _ => T("지원되지 않음", "UNSUPPORTED")
    };

    public static string Install(InstallState state) => state switch
    {
        InstallState.Installed => T("설치됨", "INSTALLED"),
        InstallState.NotInstalled => T("설치되지 않음", "NOT INSTALLED"),
        InstallState.OutdatedVersion => T("오래된 버전", "OUTDATED VERSION"),
        InstallState.WrongArchitecture => T("아키텍처 불일치", "WRONG ARCHITECTURE"),
        InstallState.VersionMismatch => T("버전 불일치", "VERSION MISMATCH"),
        InstallState.DetectedButNotConfigured => T("발견됐으나 구성되지 않음", "DETECTED BUT NOT CONFIGURED"),
        InstallState.Checking => T("확인 중", "CHECKING"),
        _ => T("설치 필요", "INSTALLATION REQUIRED")
    };

    public static string Support(SupportLevel level) => level switch
    {
        SupportLevel.Full => T("완전 지원", "FULL SUPPORT"),
        SupportLevel.Conditional => T("조건부 지원", "CONDITIONAL SUPPORT"),
        SupportLevel.Experimental => T("실험적", "EXPERIMENTAL"),
        SupportLevel.ResourceOnly => T("리소스 전용", "RESOURCE ONLY"),
        _ => T("지원되지 않음", "NOT SUPPORTED")
    };

    public static string Method(BuildMethod method) => method switch
    {
        BuildMethod.PyInstaller => "PyInstaller",
        BuildMethod.Nuitka => "Nuitka",
        BuildMethod.WebView2 => "WebView2",
        BuildMethod.Electron => "Electron",
        BuildMethod.DotNetPublish => "dotnet publish",
        BuildMethod.MsBuild => "MSBuild",
        BuildMethod.CMake => "CMake",
        BuildMethod.MinGw => "MinGW-w64",
        BuildMethod.Jpackage => "jpackage",
        BuildMethod.GoBuild => "go build",
        BuildMethod.Cargo => "cargo build",
        BuildMethod.PhpHost => T("PHP 런타임 호스트", "PHP runtime host"),
        BuildMethod.RubyHost => T("Ruby 런타임 호스트", "Ruby runtime host"),
        BuildMethod.ScriptWrapper => T("실행 래퍼", "Execution wrapper"),
        _ => T("없음", "None")
    };

    public static string Stage(BuildStage stage) => stage switch
    {
        BuildStage.Preparing => T("프로젝트 준비", "Preparing Project"),
        BuildStage.CheckingRequirements => T("요구 사항 확인", "Checking Requirements"),
        BuildStage.ResolvingDependencies => T("의존성 해결", "Resolving Dependencies"),
        BuildStage.ConfiguringBuild => T("빌드 구성", "Configuring Build"),
        BuildStage.Compiling => T("소스 컴파일", "Compiling Source"),
        BuildStage.Packaging => T("애플리케이션 패키징", "Packaging Application"),
        BuildStage.Verifying => T("실행 파일 검증", "Verifying Executable"),
        BuildStage.Completed => T("빌드 완료", "Build Completed"),
        _ => T("빌드 실패", "Build Failed")
    };

    public static string Architecture(ArchitectureKind kind) => kind switch
    {
        ArchitectureKind.X86 => "x86",
        ArchitectureKind.Arm64 => "ARM64",
        _ => "x64"
    };
}
