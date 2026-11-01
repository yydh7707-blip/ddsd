using DEX.Core.Localization;
using DEX.Core.Security;

namespace DEX.Core.Installation;

public sealed class OfficialDownloadService
{
    public void OpenDownload(string toolId)
    {
        var tool = OfficialToolCatalog.Get(toolId);
        OpenUrl(tool.DownloadUrl);
    }

    public void OpenGuide(string toolId)
    {
        var tool = OfficialToolCatalog.Get(toolId);
        OpenUrl(tool.GuideUrl ?? tool.DownloadUrl);
    }

    public void OpenUrl(string url)
    {
        UrlGuard.EnsureAllowed(url);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    public void OpenFolder(string path)
    {
        SecurityManager.EnsureSafePath(path);
        if (!Directory.Exists(path) && !File.Exists(path))
            throw new InvalidOperationException(Text.T("경로가 없습니다.", "Path does not exist."));
        var full = Path.GetFullPath(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = File.Exists(full) ? "/select,\"" + full + "\"" : "\"" + full + "\"",
            UseShellExecute = false
        });
    }

    public string Describe(string toolId)
    {
        var tool = OfficialToolCatalog.Get(toolId);
        return Text.Language == "en" ? tool.DescriptionEn : tool.DescriptionKo;
    }
}
