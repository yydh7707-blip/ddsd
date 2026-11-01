using DEX.Core.Models;

namespace DEX.Core.Analyzers;

public sealed class LanguageDetector
{
    public IReadOnlyList<LanguageHit> Detect(IReadOnlyList<ScannedFile> files)
    {
        var scores = new Dictionary<ProjectKind, (int Score, string Evidence)>();
        void Add(ProjectKind kind, int score, string evidence)
        {
            if (!scores.TryGetValue(kind, out var current) || score > current.Score)
                scores[kind] = (score, evidence);
            else if (score == current.Score)
                scores[kind] = (score, current.Evidence + ", " + evidence);
        }

        bool Has(string name) => files.Any(f => f.RelativePath.Equals(name, StringComparison.OrdinalIgnoreCase)
            || f.RelativePath.EndsWith(Path.DirectorySeparatorChar + name, StringComparison.OrdinalIgnoreCase)
            || f.RelativePath.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
        bool Ext(string ext) => files.Any(f => f.Extension == ext);
        int Count(string ext) => files.Count(f => f.Extension == ext);

        if (files.Any(f => f.Extension is ".sln" or ".csproj" or ".vbproj" or ".fsproj"))
            Add(ProjectKind.DotNet, 100, "project file");
        if (Ext(".cs") || Ext(".vb") || Ext(".fs"))
            Add(ProjectKind.DotNet, 45, "source file");
        if (Ext(".vcxproj"))
            Add(ProjectKind.Cpp, 100, "vcxproj");
        if (Has("CMakeLists.txt"))
            Add(ProjectKind.Cpp, 85, "CMakeLists.txt");
        if (Has("Makefile") && (Ext(".c") || Ext(".cpp") || Ext(".cc") || Ext(".h")))
            Add(ProjectKind.Cpp, 60, "Makefile");
        if (Ext(".c") || Ext(".cpp") || Ext(".cc") || Ext(".hpp"))
            Add(ProjectKind.Cpp, 40, "C/C++ source");
        if (Has("pom.xml") || Has("build.gradle") || Has("build.gradle.kts"))
            Add(ProjectKind.Java, 100, "build file");
        if (Ext(".java"))
            Add(ProjectKind.Java, 45, ".java");
        if (Ext(".jar") && !Ext(".java") && !Has("pom.xml"))
            Add(ProjectKind.Java, 70, ".jar");
        if (Has("go.mod"))
            Add(ProjectKind.Go, 100, "go.mod");
        else if (Ext(".go"))
            Add(ProjectKind.Go, 50, ".go");
        if (Has("Cargo.toml"))
            Add(ProjectKind.Rust, 100, "Cargo.toml");
        else if (Ext(".rs"))
            Add(ProjectKind.Rust, 50, ".rs");
        if (Has("package.json"))
            Add(ProjectKind.Web, 90, "package.json");
        if (Ext(".html") || Ext(".htm"))
            Add(ProjectKind.Web, 55, "html");
        if (Has("requirements.txt") || Has("pyproject.toml") || Has("setup.py"))
            Add(ProjectKind.Python, 95, "python project file");
        if (Ext(".py") || Ext(".pyw"))
            Add(ProjectKind.Python, 50 + Math.Min(Count(".py"), 10), ".py");
        if (Ext(".pyc"))
            Add(ProjectKind.Python, 35, ".pyc");
        if (Has("composer.json"))
            Add(ProjectKind.Php, 90, "composer.json");
        else if (Ext(".php"))
            Add(ProjectKind.Php, 50, ".php");
        if (Has("Gemfile"))
            Add(ProjectKind.Ruby, 90, "Gemfile");
        else if (Ext(".rb"))
            Add(ProjectKind.Ruby, 40, ".rb");
        if (Ext(".bat") || Ext(".cmd") || Ext(".ps1"))
            Add(ProjectKind.Script, 35, "script");
        if (Ext(".swift"))
            Add(ProjectKind.Swift, 60, ".swift");
        if (Ext(".sh"))
            Add(ProjectKind.Unknown, 20, ".sh");

        var resourceExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".json", ".xml", ".txt", ".yaml", ".yml", ".ini", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".css"
        };
        if (scores.Count == 0 && files.All(f => resourceExt.Contains(f.Extension) || f.Extension == ""))
            Add(ProjectKind.Resource, 30, "resource files only");
        if (scores.Count == 0 && files.Any(f => f.Extension == ".css") && !Ext(".html") && !Ext(".htm") && !Has("package.json"))
            Add(ProjectKind.Resource, 40, "css without host");
        if (files.Any(f => f.Extension == ".json") && scores.Count == 0)
            Add(ProjectKind.Resource, 25, "json");

        return scores
            .OrderByDescending(p => p.Value.Score)
            .Select(p => new LanguageHit(p.Key, p.Value.Score, p.Value.Evidence))
            .ToList();
    }
}
