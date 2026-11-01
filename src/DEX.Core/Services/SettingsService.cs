using System.Text.Json;
using DEX.Core.Localization;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.Services;

public sealed class SettingsService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public SettingsService()
    {
        _path = Path.Combine(SecurityManager.DataRoot(), "settings.json");
    }

    public AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _json) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            settings = new AppSettings();
        }
        if (string.IsNullOrWhiteSpace(settings.DefaultOutputDirectory))
            settings.DefaultOutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DEX Output");
        if (settings.Theme != "Holographic")
            settings.Theme = "Holographic";
        if (settings.Language is not ("ko" or "en"))
            settings.Language = "en";
        Text.Language = settings.Language;
        Directory.CreateDirectory(settings.DefaultOutputDirectory);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Text.Language = settings.Language;
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _json));
    }
}
