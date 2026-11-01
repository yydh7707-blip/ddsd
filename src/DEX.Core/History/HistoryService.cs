using System.Text.Json;
using DEX.Core.Models;
using DEX.Core.Security;

namespace DEX.Core.History;

public sealed class HistoryService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly object _gate = new();

    public HistoryService()
    {
        _path = Path.Combine(SecurityManager.DataRoot(), "history.json");
    }

    public IReadOnlyList<HistoryEntry> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return [];
            try
            {
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_path), _json) ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public HistoryEntry Add(HistoryEntry entry)
    {
        lock (_gate)
        {
            var all = Load().ToList();
            all.Insert(0, entry);
            if (all.Count > 200)
                all.RemoveRange(200, all.Count - 200);
            File.WriteAllText(_path, JsonSerializer.Serialize(all, _json));
            return entry;
        }
    }

    public string SaveLog(string id, string log)
    {
        var dir = Path.Combine(SecurityManager.DataRoot(), "logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".log");
        File.WriteAllText(path, log);
        return path;
    }

    public void DeleteLogs()
    {
        var dir = Path.Combine(SecurityManager.DataRoot(), "logs");
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
    }
}
