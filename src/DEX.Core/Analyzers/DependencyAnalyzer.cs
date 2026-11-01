using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DEX.Core.Models;

namespace DEX.Core.Analyzers;

public sealed class DependencyAnalyzer
{
    public IReadOnlyList<DependencyItem> Analyze(ProjectKind kind, IReadOnlyList<ScannedFile> files)
    {
        var items = new List<DependencyItem>();
        try
        {
            switch (kind)
            {
                case ProjectKind.Python:
                    ParseRequirements(files, items);
                    break;
                case ProjectKind.Web:
                    ParsePackageJson(files, items);
                    break;
                case ProjectKind.DotNet:
                    ParseCsproj(files, items);
                    break;
                case ProjectKind.Go:
                    ParseGoMod(files, items);
                    break;
                case ProjectKind.Java:
                    ParsePom(files, items);
                    break;
                case ProjectKind.Rust:
                    ParseCargo(files, items);
                    break;
                case ProjectKind.Php:
                    ParseComposer(files, items);
                    break;
            }
        }
        catch
        {
            items.Add(new DependencyItem("(unparsed)", null, "parser", true));
        }
        return items;
    }

    public string? PythonRequirement(IReadOnlyList<ScannedFile> files)
    {
        var pyproject = files.FirstOrDefault(f => f.RelativePath.EndsWith("pyproject.toml", StringComparison.OrdinalIgnoreCase));
        if (pyproject is null)
            return null;
        var text = File.ReadAllText(pyproject.FullPath);
        var match = Regex.Match(text, "requires-python\\s*=\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    public string? NodeRequirement(IReadOnlyList<ScannedFile> files)
    {
        var pkg = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("package.json", StringComparison.OrdinalIgnoreCase));
        if (pkg is null)
            return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(pkg.FullPath));
        if (doc.RootElement.TryGetProperty("engines", out var engines) && engines.TryGetProperty("node", out var node))
            return node.GetString();
        return null;
    }

    public string? GoRequirement(IReadOnlyList<ScannedFile> files)
    {
        var mod = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("go.mod", StringComparison.OrdinalIgnoreCase));
        if (mod is null)
            return null;
        var match = Regex.Match(File.ReadAllText(mod.FullPath), @"^go\s+(\d+\.\d+(?:\.\d+)?)", RegexOptions.Multiline);
        return match.Success ? ">=" + match.Groups[1].Value : null;
    }

    private static void ParseRequirements(IReadOnlyList<ScannedFile> files, List<DependencyItem> items)
    {
        var req = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase));
        if (req is null)
            return;
        foreach (var line in File.ReadLines(req.FullPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            var name = Regex.Split(trimmed, "[<>=!~]")[0].Trim();
            if (name.Length > 0)
                items.Add(new DependencyItem(name, null, "requirements.txt", false));
        }
    }

    private static void ParsePackageJson(IReadOnlyList<ScannedFile> files, List<DependencyItem> items)
    {
        var pkg = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("package.json", StringComparison.OrdinalIgnoreCase));
        if (pkg is null)
            return;
        using var doc = JsonDocument.Parse(File.ReadAllText(pkg.FullPath));
        AddDeps(doc.RootElement, "dependencies", "package.json", false, items);
        AddDeps(doc.RootElement, "devDependencies", "package.json", true, items);
    }

    private static void AddDeps(JsonElement root, string prop, string source, bool optional, List<DependencyItem> items)
    {
        if (!root.TryGetProperty(prop, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return;
        foreach (var item in obj.EnumerateObject())
            items.Add(new DependencyItem(item.Name, item.Value.GetString(), source, optional));
    }

    private static void ParseCsproj(IReadOnlyList<ScannedFile> files, List<DependencyItem> items)
    {
        foreach (var file in files.Where(f => f.Extension == ".csproj"))
        {
            var doc = XDocument.Load(file.FullPath);
            foreach (var node in doc.Descendants().Where(e => e.Name.LocalName == "PackageReference"))
            {
                var include = (string?)node.Attribute("Include") ?? node.Elements().FirstOrDefault(e => e.Name.LocalName == "Include")?.Value;
                var version = (string?)node.Attribute("Version") ?? node.Elements().FirstOrDefault(e => e.Name.LocalName == "Version")?.Value;
                if (!string.IsNullOrWhiteSpace(include))
                    items.Add(new DependencyItem(include, version, file.RelativePath, false));
            }
        }
    }

    private static void ParseGoMod(IReadOnlyList<ScannedFile> files, List<DependencyItem> items)
    {
        var mod = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("go.mod", StringComparison.OrdinalIgnoreCase));
        if (mod is null)
            return;
        foreach (Match match in Regex.Matches(File.ReadAllText(mod.FullPath), @"^\s+([A-Za-z0-9_./\-]+)\s+v(\d+\.\d+\.\d+)", RegexOptions.Multiline))
            items.Add(new DependencyItem(match.Groups[1].Value, match.Groups[2].Value, "go.mod", false));
    }

    private static void ParsePom(IReadOnlyList<ScannedFile> files, List<DependencyItem> items)
    {
        var pom = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("pom.xml", StringComparison.OrdinalIgnoreCase));
        if (pom is null)
            return;
        var doc = XDocument.Load(pom.FullPath);
        foreach (var dep in doc.Descendants().Where(e => e.Name.LocalName == "dependency"))
        {
            var id = dep.Elements().FirstOrDefault(e => e.Name.LocalName == "artifactId")?.Value;
            var ver = dep.Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
                items.Add(new DependencyItem(id, ver, "pom.xml", false));
        }
    }

    private static void ParseCargo(IReadOnlyList<ScannedFile> files, List<DependencyItem> items)
    {
        var cargo = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase));
        if (cargo is null)
            return;
        foreach (Match match in Regex.Matches(File.ReadAllText(cargo.FullPath), @"^([A-Za-z0-9_\-]+)\s*=\s*""([^""]+)""", RegexOptions.Multiline))
        {
            if (match.Groups[1].Value is "name" or "version" or "edition")
                continue;
            items.Add(new DependencyItem(match.Groups[1].Value, match.Groups[2].Value, "Cargo.toml", false));
        }
    }

    private static void ParseComposer(IReadOnlyList<ScannedFile> files, List<DependencyItem> items)
    {
        var file = files.FirstOrDefault(f => Path.GetFileName(f.RelativePath).Equals("composer.json", StringComparison.OrdinalIgnoreCase));
        if (file is null)
            return;
        using var doc = JsonDocument.Parse(File.ReadAllText(file.FullPath));
        AddDeps(doc.RootElement, "require", "composer.json", false, items);
    }
}
