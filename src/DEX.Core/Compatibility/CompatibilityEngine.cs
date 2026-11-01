using DEX.Core.Probes;
using DEX.Core.Localization;
using DEX.Core.Models;

namespace DEX.Core.Compatibility;

public sealed class CompatibilityEngine
{
    public CompatibilityReport Evaluate(ProjectAnalysis analysis, IReadOnlyList<ToolStatus> tools, string? selectedEntry)
    {
        var byId = tools.ToDictionary(t => t.ToolId, StringComparer.OrdinalIgnoreCase);
        var results = new List<RequirementResult>();
        foreach (var requirement in analysis.Requirements)
        {
            byId.TryGetValue(requirement.ToolId, out var status);
            status ??= new ToolStatus
            {
                ToolId = requirement.ToolId,
                Name = requirement.ToolId,
                State = InstallState.NotInstalled
            };
            var satisfied = !requirement.IsRequired || IsSatisfied(status, requirement.RequiredVersion);
            results.Add(new RequirementResult { Requirement = requirement, Status = status, Satisfied = satisfied });
        }

        var missing = results.Where(r => r.Requirement.IsRequired && !r.Satisfied).ToList();
        var optional = results.Where(r => !r.Requirement.IsRequired && !r.Satisfied)
            .Select(r => r.Requirement.ToolId + ": " + r.Requirement.Reason)
            .ToList();

        if (analysis.SupportLevel is SupportLevel.NotSupported or SupportLevel.ResourceOnly || analysis.RecommendedMethod == BuildMethod.None && analysis.Kind != ProjectKind.Web)
        {
            return Report(CompatibilityStatus.Unsupported, false, analysis,
                Text.T("이 입력은 EXE로 빌드할 수 없습니다.", "This input cannot be built into an EXE."),
                Text.T("지원되는 프로젝트 구조와 소스 진입점이 필요합니다.", "A supported project structure and source entry point are required."),
                Text.T("리소스 파일이나 바이트코드만 있는 경우 변환하지 않습니다.", "Resources or bytecode alone are not converted."),
                results, optional);
        }

        if (analysis.IsAmbiguous && string.IsNullOrWhiteSpace(selectedEntry))
        {
            return Report(CompatibilityStatus.NeedsConfiguration, false, analysis,
                Text.T("어떤 프로젝트를 빌드할지 지정해야 합니다.", "Choose which project to build."),
                string.Join(", ", analysis.AmbiguousKinds),
                Text.T("진입점 또는 프로젝트 파일을 선택하세요.", "Select an entry point or project file."),
                results, optional);
        }

        if (analysis.EntryPointRequiredFromUser && string.IsNullOrWhiteSpace(selectedEntry) && string.IsNullOrWhiteSpace(analysis.EntryPoint))
        {
            return Report(CompatibilityStatus.NeedsConfiguration, false, analysis,
                Text.T("실행 진입점을 찾지 못했거나 여러 개입니다.", "The entry point is missing or ambiguous."),
                Text.T("진입점 파일을 직접 지정해야 합니다.", "You need to select the entry file."),
                Text.T("목록에서 진입점을 선택한 뒤 다시 검사하세요.", "Select an entry point and recheck."),
                results, optional);
        }

        if (missing.Any(m => m.Status.State is InstallState.VersionMismatch or InstallState.OutdatedVersion))
        {
            var first = missing.First(m => m.Status.State is InstallState.VersionMismatch or InstallState.OutdatedVersion);
            return Report(CompatibilityStatus.VersionMismatch, false, analysis,
                Text.T("설치 버전이 프로젝트 요구 버전과 다릅니다.", "The installed version does not match the project requirement."),
                (first.Requirement.RequiredVersion ?? "") + " / " + (first.Status.Version ?? Text.T("없음", "none")),
                Text.T("공식 페이지에서 필요한 버전을 설치한 뒤 재검사하세요.", "Install the required version from the official page, then recheck."),
                results, optional);
        }

        if (missing.Count > 0)
        {
            return Report(CompatibilityStatus.NeedsSetup, false, analysis,
                Text.T("필수 개발 도구가 설치되어 있지 않습니다.", "A required development tool is not installed."),
                string.Join(", ", missing.Select(m => m.Status.Name)),
                Text.T(                    "공식 웹사이트에서 직접 설치한 뒤 Recheck를 누르세요. COMPIVOX는 설치 파일을 받지 않습니다.",
                    "Install it yourself from the official website, then press Recheck. COMPIVOX does not download installers."),
                results, optional);
        }

        if (analysis.Dependencies.Any(d => d.Name == "(unparsed)"))
        {
            return Report(CompatibilityStatus.DependencyError, false, analysis,
                Text.T("의존성 파일을 해석하지 못했습니다.", "A dependency file could not be parsed."),
                Text.T("손상되었거나 지원하지 않는 형식입니다.", "It is damaged or in an unsupported format."),
                Text.T("의존성 파일을 확인한 뒤 다시 분석하세요.", "Fix the dependency file and analyze again."),
                results, optional);
        }

        var partial = analysis.SupportLevel is SupportLevel.Experimental or SupportLevel.Conditional;
        return Report(partial ? CompatibilityStatus.PartiallySupported : CompatibilityStatus.Ready, true, analysis,
            partial
                ? Text.T("빌드는 가능하지만 제한 또는 실험적 방식을 사용합니다.", "A build is possible, with limits or an experimental method.")
                : Text.T("필수 조건이 충족되었습니다.", "All required conditions are met."),
            analysis.OutputType,
            partial
                ? Text.T("출력 종류 설명을 확인한 뒤 빌드하세요.", "Read the output-type note before building.")
                : Text.T("Build EXE를 실행할 수 있습니다.", "Build EXE can run."),
            results, optional);
    }

    private static bool IsSatisfied(ToolStatus status, string? requiredVersion)
    {
        if (status.State is InstallState.NotInstalled or InstallState.InstallationRequired or InstallState.WrongArchitecture or InstallState.DetectedButNotConfigured)
            return false;
        if (status.State == InstallState.VersionMismatch)
            return false;
        if (!string.IsNullOrWhiteSpace(requiredVersion) && status.Version is not null && !VersionParser.Satisfies(status.Version, requiredVersion))
            return false;
        return status.State is InstallState.Installed or InstallState.OutdatedVersion;
    }

    private static CompatibilityReport Report(
        CompatibilityStatus status, bool allowed, ProjectAnalysis analysis, string problem, string needed, string resolution,
        IReadOnlyList<RequirementResult> results, IReadOnlyList<string> optional) => new()
    {
        Status = status,
        BuildAllowed = allowed && analysis.RecommendedMethod != BuildMethod.None,
        Summary = Text.Status(status),
        Problem = problem,
        Needed = needed,
        Resolution = resolution,
        Results = results,
        OptionalNotes = optional
    };
}
