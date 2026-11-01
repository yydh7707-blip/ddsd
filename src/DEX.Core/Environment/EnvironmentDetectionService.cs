using System.Text.RegularExpressions;
using DEX.Core.Installation;
using DEX.Core.Localization;
using DEX.Core.Models;

namespace DEX.Core.Probes;

public sealed class EnvironmentDetectionService
{
    private readonly ProcessRunner _runner;
    private readonly ToolLocator _locator;

    public EnvironmentDetectionService(ProcessRunner runner, ToolLocator locator)
    {
        _runner = runner;
        _locator = locator;
    }

    public async Task<IReadOnlyList<ToolStatus>> CheckAllAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ToolStatus>();
        foreach (var tool in OfficialToolCatalog.All)
            results.Add(await CheckAsync(tool.Id, null, cancellationToken));
        return results;
    }

    public async Task<IReadOnlyList<ToolStatus>> CheckRequiredAsync(
        IEnumerable<string> toolIds,
        IReadOnlyDictionary<string, string?> requiredVersions,
        CancellationToken cancellationToken = default)
    {
        var list = new List<ToolStatus>();
        foreach (var id in toolIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            requiredVersions.TryGetValue(id, out var required);
            list.Add(await CheckAsync(id, required, cancellationToken));
        }
        return list;
    }

    public async Task<ToolStatus> CheckAsync(string toolId, string? requiredVersion, CancellationToken cancellationToken = default)
    {
        var tool = OfficialToolCatalog.Find(toolId);
        if (tool is null)
        {
            return new ToolStatus
            {
                ToolId = toolId,
                Name = toolId,
                State = InstallState.NotInstalled,
                Detail = Text.T("등록되지 않은 도구입니다.", "Tool is not registered.")
            };
        }

        if (toolId == "webview2")
            return WebView2Status(tool, requiredVersion);
        if (toolId == "dotnet-framework")
            return FrameworkStatus(tool, requiredVersion);
        if (toolId == "vs-buildtools")
            return await VsStatusAsync(tool, cancellationToken);
        if (toolId == "npm")
            return NpmStatus(tool);
        if (toolId == "python")
            return await PythonStatusAsync(tool, requiredVersion, cancellationToken);

        var exe = tool.FileNames.Select(n => _locator.Find(n)).FirstOrDefault(p => p is not null);
        if (exe is null)
        {
            return Missing(tool);
        }

        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Contains(Path.GetDirectoryName(exe)!, StringComparison.OrdinalIgnoreCase);
        string? version = null;
        var detail = "";
        if (tool.Probes.Count > 0)
        {
            var probe = tool.Probes[0];
            var result = await _runner.RunAsync(exe, probe.Arguments, timeout: SecurityProbe(), cancellationToken: cancellationToken);
            var text = result.StandardOutput + "\n" + result.StandardError;
            version = VersionParser.Extract(text);
            if (result.TimedOut)
                detail = Text.T("버전 확인 시간이 초과되었습니다.", "Version check timed out.");
            else if (result.Killed)
                detail = Text.T("버전 확인 프로세스가 종료되었습니다.", "Version check process was terminated.");
        }

        var state = InstallState.Installed;
        if (!string.IsNullOrWhiteSpace(requiredVersion) && !VersionParser.Satisfies(version, requiredVersion))
            state = InstallState.VersionMismatch;
        if (version is null && tool.Probes.Count > 0 && state == InstallState.Installed)
            state = InstallState.DetectedButNotConfigured;

        return new ToolStatus
        {
            ToolId = tool.Id,
            Name = tool.Name,
            State = state,
            Version = version,
            ExecutablePath = exe,
            Detail = detail,
            FoundOutsidePath = !onPath,
            Architecture = _locator.ArchitectureOf(exe)
        };
    }

    private async Task<ToolStatus> PythonStatusAsync(OfficialTool tool, string? requiredVersion, CancellationToken cancellationToken)
    {
        var installs = _locator.FindPythonInstalls();
        var py = _locator.Find("py.exe");
        var versions = new List<string>();
        string? chosen = installs.FirstOrDefault();
        if (py is not null)
        {
            var listed = await _runner.RunAsync(py, ["-0p"], timeout: SecurityProbe(), cancellationToken: cancellationToken);
            foreach (Match match in Regex.Matches(listed.StandardOutput, @"-V:(\d+\.\d+)\s+\*?\s*(.+)"))
            {
                versions.Add(match.Groups[1].Value);
                var path = match.Groups[2].Value.Trim();
                if (File.Exists(path) && !installs.Contains(path, StringComparer.OrdinalIgnoreCase))
                    installs = installs.Append(path).ToList();
            }
        }

        if (installs.Count == 0 && py is null)
            return Missing(tool);

        if (!string.IsNullOrWhiteSpace(requiredVersion))
        {
            foreach (var candidate in installs)
            {
                var probed = await _runner.RunAsync(candidate, ["--version"], timeout: SecurityProbe(), cancellationToken: cancellationToken);
                var ver = VersionParser.Extract(probed.StandardOutput + probed.StandardError);
                if (ver is not null && VersionParser.Satisfies(ver, requiredVersion))
                {
                    chosen = candidate;
                    return Installed(tool, candidate, ver, versions, requiredVersion);
                }
            }
            var any = installs.FirstOrDefault() ?? py;
            var anyVer = any is null ? null : VersionParser.Extract((await _runner.RunAsync(any, ["--version"], timeout: SecurityProbe(), cancellationToken: cancellationToken)).StandardOutput);
            return new ToolStatus
            {
                ToolId = tool.Id,
                Name = tool.Name,
                State = InstallState.VersionMismatch,
                Version = anyVer,
                ExecutablePath = any,
                ExtraVersions = versions,
                Detail = Text.T($"필요 버전 {requiredVersion} 과 일치하는 Python이 없습니다.", $"No Python install matches {requiredVersion}.")
            };
        }

        chosen ??= py;
        string? version = null;
        if (chosen is not null)
        {
            var probed = await _runner.RunAsync(chosen, ["--version"], timeout: SecurityProbe(), cancellationToken: cancellationToken);
            version = VersionParser.Extract(probed.StandardOutput + probed.StandardError);
        }
        return Installed(tool, chosen!, version, versions, null);
    }

    private ToolStatus NpmStatus(OfficialTool tool)
    {
        var node = _locator.Find("node.exe");
        if (node is null)
            return Missing(tool);
        var cli = _locator.FindNpmCli(node);
        if (cli is null)
        {
            return new ToolStatus
            {
                ToolId = tool.Id,
                Name = tool.Name,
                State = InstallState.DetectedButNotConfigured,
                ExecutablePath = node,
                Detail = Text.T("Node.js는 있지만 npm-cli.js를 찾지 못했습니다.", "Node.js was found, but npm-cli.js was not.")
            };
        }
        return new ToolStatus
        {
            ToolId = tool.Id,
            Name = tool.Name,
            State = InstallState.Installed,
            ExecutablePath = cli,
            Detail = node
        };
    }

    private async Task<ToolStatus> VsStatusAsync(OfficialTool tool, CancellationToken cancellationToken)
    {
        var vswhere = _locator.FindMsBuild();
        if (vswhere is null || !File.Exists(vswhere))
            return Missing(tool);
        var result = await _runner.RunAsync(
            vswhere,
            ["-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath"],
            timeout: SecurityProbe(),
            cancellationToken: cancellationToken);
        var install = result.StandardOutput.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install))
        {
            return new ToolStatus
            {
                ToolId = tool.Id,
                Name = tool.Name,
                State = InstallState.NotInstalled,
                ExecutablePath = vswhere,
                Detail = Text.T("C++ 도구(MSVC, Windows SDK)가 선택된 설치를 찾지 못했습니다. 전체 IDE가 아니라 Build Tools의 'Desktop development with C++' 구성 요소가 필요합니다.",
                    "No installation with the C++ tools was found. Install Build Tools with the Desktop development with C++ workload, not necessarily the full IDE.")
            };
        }
        var msbuild = Directory.EnumerateFiles(install, "MSBuild.exe", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Contains(@"\Bin\MSBuild.exe", StringComparison.OrdinalIgnoreCase));
        var cl = Directory.EnumerateFiles(Path.Combine(install, "VC", "Tools", "MSVC"), "cl.exe", SearchOption.AllDirectories)
            .FirstOrDefault(p => p.Contains(@"\bin\Hostx64\x64\", StringComparison.OrdinalIgnoreCase));
        return new ToolStatus
        {
            ToolId = tool.Id,
            Name = tool.Name,
            State = msbuild is null ? InstallState.DetectedButNotConfigured : InstallState.Installed,
            Version = new DirectoryInfo(install).Name,
            ExecutablePath = msbuild ?? vswhere,
            Detail = cl is null
                ? Text.T("MSBuild는 찾았지만 cl.exe를 확인하지 못했습니다.", "MSBuild was found, but cl.exe was not confirmed.")
                : cl,
            Architecture = ArchitectureKind.X64
        };
    }

    private static ToolStatus WebView2Status(OfficialTool tool, string? requiredVersion)
    {
        var version = ToolLocator.ReadWebView2Version();
        if (version is null)
            return Missing(tool);
        var mismatch = !string.IsNullOrWhiteSpace(requiredVersion) && !VersionParser.Satisfies(version, requiredVersion);
        return new ToolStatus
        {
            ToolId = tool.Id,
            Name = tool.Name,
            State = mismatch ? InstallState.VersionMismatch : InstallState.Installed,
            Version = version
        };
    }

    private static ToolStatus FrameworkStatus(OfficialTool tool, string? requiredVersion)
    {
        var version = string.IsNullOrWhiteSpace(requiredVersion) ? "4.8" : requiredVersion.TrimStart('v', 'V');
        if (version.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            version = version["net".Length..].Insert(1, ".");
        var ok = ToolLocator.HasTargetingPack(version.StartsWith("4") && !version.Contains('.') ? version.Insert(1, ".") : version);
        return new ToolStatus
        {
            ToolId = tool.Id,
            Name = tool.Name,
            State = ok ? InstallState.Installed : InstallState.NotInstalled,
            Version = ok ? version : null,
            Detail = ok ? null : Text.T($"Targeting Pack v{version} 이 없습니다.", $"Targeting Pack v{version} was not found.")
        };
    }

    private ToolStatus Installed(OfficialTool tool, string path, string? version, IReadOnlyList<string> extra, string? required)
    {
        var state = InstallState.Installed;
        if (!string.IsNullOrWhiteSpace(required) && !VersionParser.Satisfies(version, required))
            state = InstallState.VersionMismatch;
        var onPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Contains(Path.GetDirectoryName(path) ?? "\0", StringComparison.OrdinalIgnoreCase);
        return new ToolStatus
        {
            ToolId = tool.Id,
            Name = tool.Name,
            State = state,
            Version = version,
            ExecutablePath = path,
            ExtraVersions = extra,
            FoundOutsidePath = !onPath,
            Architecture = _locator.ArchitectureOf(path),
            Detail = !onPath
                ? Text.T(                    "설치 경로는 확인했지만 현재 프로세스 PATH에는 없습니다. 알려진 경로로 검사했습니다. 필요하면 COMPIVOX를 다시 시작하세요.",
                    "The install path was found, but it is not on this process PATH. COMPIVOX checked known locations. Restart COMPIVOX if detection still fails.")
                : null
        };
    }

    private static ToolStatus Missing(OfficialTool tool) => new()
    {
        ToolId = tool.Id,
        Name = tool.Name,
        State = InstallState.NotInstalled,
        Detail = Text.Language == "en" ? tool.DescriptionEn : tool.DescriptionKo
    };

    private static TimeSpan SecurityProbe() => TimeSpan.FromSeconds(20);
}
