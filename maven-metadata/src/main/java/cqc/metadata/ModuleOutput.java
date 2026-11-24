package cqc.metadata;

import org.apache.maven.project.MavenProject;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;

/** Owns a module's temporary output paths within this reactor invocation. */
record ModuleOutput(Path directory) {
    private static final String KEY = ModuleOutput.class.getName();

    static ModuleOutput create(Path reactorDirectory, MavenProject project) throws IOException {
        var output = new ModuleOutput(Files.createTempDirectory(reactorDirectory, "module-"));
        project.setContextValue(KEY, output);
        return output;
    }

    static ModuleOutput from(MavenProject project) {
        return (ModuleOutput) project.getContextValue(KEY);
    }

    Path metadata() { return directory.resolve("metadata.xml"); }
    Path data() { return directory.resolve("jacoco.exec"); }
    Path report() { return directory.resolve("jacoco.xml"); }
}
