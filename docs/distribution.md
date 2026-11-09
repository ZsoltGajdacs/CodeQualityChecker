# macOS arm64 distribution

The initial distribution is a directory/tarball, not a global installer. Keep `cqc`, managed libraries, `java-worker.jar`, `runtime/`, and legal notices together. The host is published self-contained for `osx-arm64`; the Java runtime is a trimmed `jlink` image. Source-only analysis therefore needs neither a host .NET runtime nor host Java. Explicit execution still needs the target project's toolchains.

## Build prerequisites and pins

- .NET SDK 10.0.403 (`global.json`); NuGet versions and transitive dependencies are recorded in project lock files.
- Maven, with Java worker dependencies/plugins pinned in its POM.
- `JAVA_HOME` pointing to JetBrains Runtime SDK `25.0.2+10-b300.57`, macOS aarch64, JBRSDK/JCEF edition.
- Python 3 for packaging metadata and MCP smoke testing.

`upstream.lock.json` pins the JDK release-file hash and the SHA-256 of every transitively linked JMOD. The image includes `java.se` for complete standard Java API availability, plus compiler/support modules; trimming away `java.sql` or `java.net.http` would incorrectly make ordinary Java source unresolved. The packaging script rejects a mismatched runtime or module set rather than silently using a different JDK. Runtime project/source information: [JetBrainsRuntime](https://github.com/JetBrains/JetBrainsRuntime). The installed JDK's release metadata records source revision `c628622cebf8` and vendor build details. This repository does not automatically download or install a host JDK.

Run:

```sh
export JAVA_HOME=/absolute/path/to/pinned/jbr-sdk/Contents/Home
./scripts/package-macos.sh
```

The script builds the Java worker, publishes the self-contained server, links the pinned Java modules, gathers notices, records build/file hashes, and starts a real MCP client smoke test. Tool dependency builds use dedicated public Maven Central settings without changing host settings or target-project execution policy. The smoke process removes Java/.NET discovery environment variables and restricts `PATH` to a temporary directory containing only Git. It discovers all seven tools, invokes the six inspection/check tools against a mixed Java/C# source fixture, and reconstructs reports through `get_report_page` raw pages. It verifies bounded messages, both language analyzers, and unchanged input bytes.

Outputs:

- `dist/cqc-osx-arm64/`: usable server directory.
- `dist/cqc-osx-arm64.tar.gz`: distribution archive.
- `dist/cqc-osx-arm64.tar.gz.sha256`: archive checksum.
- `build-manifest.json` inside the directory: package file hashes and build/runtime versions.

For an independent rerun:

```sh
python3 scripts/smoke-package.py dist/cqc-osx-arm64
```

The package includes Java module legal notices, .NET runtime notices, NuGet license declarations/notices, Java dependency notices, upstream attribution, and documentation. Preserve them when redistributing. Packaging does not sign or notarize the executable and does not claim a public release has been published. Native Windows/Linux/Intel and remote HTTP distributions are deferred.
