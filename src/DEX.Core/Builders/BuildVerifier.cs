using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Builders;

public sealed class BuildVerifier
{
    public BuildResult Verify(string? exePath, string outputDirectory, OutputKind kind, string log, TimeSpan duration, IReadOnlyList<string> warnings, string description)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return Fail(Text.T("빌드가 끝났지만 EXE 파일이 없습니다. 성공으로 처리하지 않습니다.",
                "The build finished but the EXE does not exist. This is a failure."), log, duration, BuildStage.Verifying);
        }
        var info = new FileInfo(exePath);
        if (info.Length < 64)
            return Fail(Text.T("EXE 크기가 비정상입니다.", "EXE size is not valid."), log, duration, BuildStage.Verifying);
        var pe = PeVerifier.Read(exePath);
        if (!pe.IsPe)
            return Fail(Text.T("출력이 PE 실행 파일이 아닙니다. 확장자만 바뀐 파일은 거부합니다.",
                "Output is not a PE executable. A renamed file is rejected."), log, duration, BuildStage.Verifying);
        return new BuildResult
        {
            Success = true,
            ExecutablePath = exePath,
            OutputDirectory = outputDirectory,
            OutputKind = kind,
            Architecture = pe.Architecture?.ToString() ?? pe.Machine,
            Log = log,
            Duration = duration,
            Warnings = warnings,
            OutputDescription = description + " " + pe.Machine
        };
    }

    public static BuildResult Fail(string error, string log, TimeSpan duration, BuildStage stage) => new()
    {
        Success = false,
        Error = error,
        FailureStage = Text.Stage(stage),
        Log = log,
        Duration = duration
    };
}
