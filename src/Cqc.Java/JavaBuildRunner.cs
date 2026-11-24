using Cqc.Core;

namespace Cqc.Java;

/// <summary>Runs one Maven reactor with coverage and evaluated-model collection enabled.</summary>
internal sealed class JavaBuildRunner(string root, string runDirectory, SortedDictionary<string, string> versions)
{
    internal async Task<string> BuildAsync(ProjectModule module, CancellationToken token)
    {
        var project = Path.Combine(root, module.Path);
        var directory = Path.GetDirectoryName(project)!;
        var tool = MavenTool.Select(root, project);
        if (tool != "mvn" && (File.GetAttributes(tool) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Maven Wrapper must be a regular executable file: " + tool);

        var output = Path.Combine(runDirectory, "reactor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        versions["maven:" + module.Path] = (await MavenTool.RunRequired(tool, ["--version"], directory, token)).Output.Trim();
        await MavenTool.RunRequired(tool, ["-B", "-f", project,
            "-Dmaven.ext.class.path=" + MavenCollector.Find(),
            "-Dcqc.jacoco.fallbackVersion=" + JavaExecution.JacocoVersion,
            "-Dcqc.projectRoot=" + root,
            "-Dcqc.reactorDirectory=" + output, "test",
            "org.apache.maven.plugins:maven-dependency-plugin:3.9.0:build-classpath",
            "-DincludeScope=test"], directory, token);
        return output;
    }
}
