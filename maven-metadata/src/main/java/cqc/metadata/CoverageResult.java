package cqc.metadata;

import org.apache.maven.project.MavenProject;

import java.nio.file.Path;

/** The module-local coverage output selected by the Maven lifecycle participant. */
record CoverageResult(String version, Path report, String reason) {
    private static final String KEY = CoverageResult.class.getName();

    void attachTo(MavenProject project) {
        project.setContextValue(KEY, this);
    }

    static CoverageResult from(MavenProject project) {
        return (CoverageResult) project.getContextValue(KEY);
    }
}
