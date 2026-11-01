namespace DEX.Core.Models;

public enum SupportLevel
{
    Full,
    Conditional,
    Experimental,
    ResourceOnly,
    NotSupported
}

public enum CompatibilityStatus
{
    Ready,
    NeedsSetup,
    VersionMismatch,
    NeedsConfiguration,
    DependencyError,
    PartiallySupported,
    Unsupported
}

public enum InstallState
{
    Checking,
    Installed,
    NotInstalled,
    OutdatedVersion,
    WrongArchitecture,
    VersionMismatch,
    DetectedButNotConfigured,
    InstallationRequired
}

public enum ArchitectureKind
{
    X64,
    X86,
    Arm64
}

public enum BuildMethod
{
    None,
    PyInstaller,
    Nuitka,
    WebView2,
    Electron,
    DotNetPublish,
    MsBuild,
    CMake,
    MinGw,
    Jpackage,
    GoBuild,
    Cargo,
    PhpHost,
    RubyHost,
    ScriptWrapper
}

public enum BuildStage
{
    Preparing,
    CheckingRequirements,
    ResolvingDependencies,
    ConfiguringBuild,
    Compiling,
    Packaging,
    Verifying,
    Completed,
    Failed
}

public enum ProjectKind
{
    Unknown,
    Python,
    Web,
    DotNet,
    Cpp,
    Java,
    Go,
    Rust,
    Php,
    Ruby,
    Script,
    Swift,
    Resource
}

public enum OutputKind
{
    None,
    SingleFileExecutable,
    ApplicationFolder,
    Installer,
    RuntimeHost,
    Wrapper
}
