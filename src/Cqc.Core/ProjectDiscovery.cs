using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Cqc.Core;

public static class ProjectDiscovery
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { ".git", ".cqc", ".backlog", ".serena", "bin", "obj", "target", "node_modules", "vendor", "dist", "TestResults" };
    private static readonly HashSet<string> OtherExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".py", ".go", ".rs", ".js", ".jsx", ".ts", ".tsx", ".clj", ".kt", ".vb", ".fs" };

    public static async Task<ProjectSnapshot> DiscoverAsync(AnalysisRequest request, QualityConfig config, CancellationToken token, IEnumerable<LanguageDefinition> languages)
    {
        var definitions = languages.ToArray();
        var root = new DirectoryInfo(Path.GetFullPath(request.ProjectRoot));
        Validate(root, request);
        return await new DiscoveryScan(root, definitions).RunAsync(request, config, token);
    }

    private static void Validate(DirectoryInfo root, AnalysisRequest request)
    {
        if (!root.Exists) throw new ArgumentException("Project root must be an existing directory.");
        if (request.Scope is not ("repository" or "paths" or "changed")) throw new ArgumentException("Scope must be repository, paths, or changed.");
        if (request.TimeoutSeconds is < 1 or > 3600) throw new ArgumentException("Timeout must be between 1 and 3600 seconds.");
    }

    public static bool Glob(string path, string pattern)
    {
        return CreateMatcher(pattern).IsMatch(path);
    }

    private static Regex CreateMatcher(string pattern)
    {
        var regex = Regex.Escape(pattern.Replace('\\', '/')).Replace("\\*\\*/", "(?:.*/)?").Replace("\\*\\*", ".*").Replace("\\*", "[^/]*").Replace("\\?", "[^/]");
        return new Regex("^" + regex + "$", RegexOptions.CultureInvariant);
    }

    private sealed class DiscoveryScan(DirectoryInfo root, LanguageDefinition[] definitions)
    {
        private readonly Dictionary<string, Regex> matchers = new(StringComparer.Ordinal);
        private bool Matches(string path, string pattern)
        {
            if (!matchers.TryGetValue(pattern, out var matcher)) matchers.Add(pattern, matcher = CreateMatcher(pattern));
            return matcher.IsMatch(path);
        }
        private bool Manifest(LanguageDefinition definition, string path) => definition.ManifestPatterns.Any(pattern => Matches(Path.GetFileName(path), pattern));
        private string[] allFiles = [];
        private string[] ignoredBuildInputs = [];
        private string[] sources = [];

        public async Task<ProjectSnapshot> RunAsync(AnalysisRequest request, QualityConfig config, CancellationToken token)
        {
            allFiles = Enumerate(root).Where(path => !config.Exclude.Any(pattern => Matches(path, pattern))).Order(StringComparer.Ordinal).ToArray();
            ignoredBuildInputs = allFiles.Where(path => definitions.Any(d => d.IgnoredBuildInputPatterns.Any(pattern => Matches(path, pattern)))).ToArray();
            allFiles = await GitScope.RemoveIgnoredAsync(root.FullName, allFiles, token);
            sources = allFiles.Where(path => definitions.Any(definition => definition.IsSource(path)))
                .Where(path => !Generated(new FileInfo(Path.Combine(root.FullName, path)))).ToArray();
            var selected = await GitScope.SelectAsync(root.FullName, sources, request, token);
            var modules = Modules();
            var fingerprint = await Fingerprint(token);
            return new(root.FullName, sources, selected, modules, fingerprint, Diagnostics(modules));
        }

        private ProjectModule[] Modules()
        {
            var modules = definitions.SelectMany(definition => allFiles.Where(path => Manifest(definition, path))
                .Select(path => definition.InspectManifest(root.FullName, path))).ToList();
            foreach (var definition in definitions.Where(definition => sources.Any(definition.IsSource)))
                if (!modules.Any(module => module.Language == definition.Language)) modules.Add(new(definition.Language, ".", "source-only", false));
            return modules.OrderBy(module => module.Path, StringComparer.Ordinal).ToArray();
        }

        private Diagnostic[] Diagnostics(ProjectModule[] modules)
        {
            var diagnostics = new List<Diagnostic>();
            var registered = definitions.SelectMany(definition => definition.SourceExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in allFiles.Select(Path.GetExtension).OfType<string>().Where(OtherExtensions.Contains)
                         .Where(extension => !registered.Contains(extension)).Select(extension => extension.ToLowerInvariant())
                         .Distinct().Order(StringComparer.Ordinal))
                diagnostics.Add(new("unsupported_language", $"Files with {extension} extension are not analyzed.") { Extension = extension });
            foreach (var module in modules.Where(module => !module.Supported))
                diagnostics.Add(new("unsupported_integration", $"{module.Integration} has source-only support.", module.Path, module.Language));
            return diagnostics.ToArray();
        }

        private async Task<string> Fingerprint(CancellationToken token)
        {
            var inputs = allFiles.Where(path => definitions.Any(definition => Fingerprinted(definition, path)))
                .Concat(ignoredBuildInputs).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            var fingerprint = new StringBuilder();
            foreach (var path in inputs)
            {
                token.ThrowIfCancellationRequested();
                await using var stream = new FileStream(Path.Combine(root.FullName, path), FileMode.Open,
                    FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = await SHA256.HashDataAsync(stream, token);
                fingerprint.Append(path).Append('\0').Append(Convert.ToHexStringLower(hash)).Append('\n');
                if (!OperatingSystem.IsWindows() && definitions.Any(d => d.ExecutableBuildInputPatterns.Any(pattern => Matches(path, pattern))))
                    fingerprint.Append("mode\0").Append(path).Append('\0').Append((int)File.GetUnixFileMode(Path.Combine(root.FullName, path))).Append('\n');
            }
            return Wire.Hash(fingerprint.ToString());
        }

        private bool Fingerprinted(LanguageDefinition definition, string path) => definition.IsSource(path) || Manifest(definition, path)
            || (definition.BuildInputPatterns ?? []).Any(pattern => Matches(path, pattern));

        private IEnumerable<string> Enumerate(DirectoryInfo directory)
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (Excluded.Contains(entry.Name)) continue;
                foreach (var file in EntryFiles(entry)) yield return file;
            }
        }

        private IEnumerable<string> EntryFiles(FileSystemInfo entry) => entry is DirectoryInfo directory
            ? Enumerate(directory) : [Relative(entry)];

        private bool Generated(FileInfo file)
        {
            var path = Relative(file);
            if (definitions.SelectMany(definition => definition.GeneratedSuffixes).Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) return true;
            return File.ReadLines(file.FullName).Take(12).Any(line => line.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase) || line.Contains("@Generated", StringComparison.Ordinal));
        }

        private string Relative(FileSystemInfo file) => Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
    }
}
