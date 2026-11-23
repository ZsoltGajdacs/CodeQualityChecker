using Cqc.Core;

namespace Cqc.Java;

/// <summary>Runs one Maven reactor with coverage and evaluated-model collection enabled.</summary>
internal sealed class JavaBuildRunner(ProjectSnapshot snapshot, string scratch, SortedDictionary<string, string> versions)
{
    internal async Task BuildAsync(ProjectModule module, CancellationToken token)
    {
        var originalTool = MavenTool.Select(snapshot.Root, Path.Combine(snapshot.Root, module.Path));
        if (originalTool != "mvn" && (File.GetAttributes(originalTool) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Maven Wrapper must be a regular executable file: " + originalTool);
        var project = Path.Combine(scratch, module.Path);
        var directory = Path.GetDirectoryName(project)!;
        var tool = MavenTool.Select(scratch, project);
        versions["maven:" + module.Path] = (await MavenTool.RunRequired(tool, ["--version"], directory, token)).Output.Trim();
        await MavenTool.RunRequired(tool, ["-B", "-f", project,
            "-Dmaven.ext.class.path=" + MavenCollector.Find(),
            "-Dcqc.jacoco.fallbackVersion=" + JavaExecution.JacocoVersion,
            "-Dcqc.scratchDirectory=" + scratch, "test",
            "org.apache.maven.plugins:maven-dependency-plugin:3.9.0:build-classpath",
            "-Dmdep.outputFile=target/cqc-classpath.txt", "-DincludeScope=test"], directory, token);
    }
}
