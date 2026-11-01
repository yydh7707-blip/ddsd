using DEX.Core.Models;

namespace DEX.Core.Installation;

public static class SupportCatalog
{
    public static IReadOnlyList<FormatEntry> All { get; } =
    [
        Entry("Python", ".py .pyw .pyc requirements.txt pyproject.toml", SupportLevel.Full, "PyInstaller / Nuitka", "Python",
            ["https://www.python.org/downloads/windows/"],
            ".pyc alone is inspected, not converted. Nuitka needs a C compiler."),
        Entry("HTML/CSS/JS", ".html .css .js .ts .tsx .jsx", SupportLevel.Conditional, "WebView2 or Electron", "Node.js, .NET SDK",
            ["https://nodejs.org/en/download", "https://dotnet.microsoft.com/download/dotnet", "https://developer.microsoft.com/microsoft-edge/webview2/"],
            "CSS alone cannot run. Electron bundles Chromium; WebView2 needs the runtime on the target PC."),
        Entry("React / Vue / Svelte", "package.json", SupportLevel.Conditional, "project build + desktop host", "Node.js, npm/pnpm/yarn, .NET SDK",
            ["https://nodejs.org/en/download"],
            "The web build must succeed before packaging."),
        Entry("C# / .NET", ".cs .csproj .sln .vb .fs", SupportLevel.Full, "dotnet publish", ".NET SDK",
            ["https://dotnet.microsoft.com/download/dotnet"],
            "SDK and Runtime are different. Self-contained output includes the runtime."),
        Entry(".NET Framework", ".csproj", SupportLevel.Conditional, "MSBuild / dotnet", "Developer Pack",
            ["https://dotnet.microsoft.com/download/dotnet-framework"],
            "Not treated as modern .NET."),
        Entry("C / C++", ".c .cpp .vcxproj CMakeLists.txt", SupportLevel.Full, "MSVC or CMake", "Build Tools, CMake",
            ["https://visualstudio.microsoft.com/downloads/", "https://cmake.org/download/"],
            "MSVC projects are not retargeted to MinGW."),
        Entry("Java", ".java .jar pom.xml build.gradle", SupportLevel.Full, "JDK + jpackage", "Temurin JDK",
            ["https://adoptium.net/temurin/releases/"],
            "A JAR is not renamed to EXE. Installer and app-image are distinguished."),
        Entry("Go", ".go go.mod", SupportLevel.Full, "go build", "Go SDK",
            ["https://go.dev/dl/"], "Requires a main package."),
        Entry("Rust", ".rs Cargo.toml", SupportLevel.Full, "cargo build --release", "Rustup, linker",
            ["https://rust-lang.org/install", "https://visualstudio.microsoft.com/downloads/"],
            "The Windows linker must be available."),
        Entry("PHP", ".php composer.json", SupportLevel.Experimental, "runtime host", "PHP, .NET SDK",
            ["https://www.php.net/downloads.php?os=windows"],
            "Not a native compile."),
        Entry("Ruby", ".rb Gemfile", SupportLevel.Experimental, "runtime host", "RubyInstaller, .NET SDK",
            ["https://rubyinstaller.org/downloads/"],
            "No verified native backend."),
        Entry("Windows Script", ".bat .cmd .ps1", SupportLevel.Conditional, "execution wrapper", ".NET SDK",
            ["https://dotnet.microsoft.com/download/dotnet"],
            "Wrapper, not a native compile."),
        Entry("Swift / shell", ".swift .sh", SupportLevel.NotSupported, "none", "none",
            [], "Unsupported when no Windows toolchain is available."),
        Entry("Resources", ".json .xml .png .svg .css .txt", SupportLevel.ResourceOnly, "none", "none",
            [], "Not standalone programs.")
    ];

    private static FormatEntry Entry(string language, string extensions, SupportLevel level, string method, string tools, IReadOnlyList<string> urls, string caveat) =>
        new()
        {
            Language = language,
            Extensions = extensions,
            Level = level,
            BuildMethod = method,
            RequiredTools = tools,
            DownloadUrls = urls,
            Caveat = caveat
        };
}
