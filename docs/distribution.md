# macOS arm64 distribution

The distribution is a directory and tarball for `osx-arm64`. Keep the self-contained server, `java-worker.jar`, `maven-metadata.jar`, `typescript-worker/`, both runtime directories, and notices together. Source analysis uses the shipped .NET, Java, and Node runtimes. Explicit Java and C# execution uses the target project's toolchains.

## Build prerequisites and pins

- .NET SDK 11.0.100 (`scripts/global.json`) for packaging. The development SDK is pinned separately in the root `global.json`. NuGet versions and transitive dependencies are recorded in project lock files.
- Maven, with Java worker dependencies/plugins pinned in its POM.
- Node 24 and npm to install the locked TypeScript worker dependencies during packaging.
- `JAVA_HOME` pointing to JetBrains Runtime SDK `25.0.2+10-b300.57`, macOS aarch64, JBRSDK/JCEF edition.
- Python 3 for packaging metadata and MCP smoke testing.

`upstream.lock.json` pins the JDK release-file hash and the SHA-256 of every transitively linked JMOD. The image includes `java.se` for complete standard Java API availability, plus compiler/support modules; trimming away `java.sql` or `java.net.http` would incorrectly make ordinary Java source unresolved. The packaging script rejects a mismatched runtime or module set rather than silently using a different JDK. Runtime project/source information: [JetBrainsRuntime](https://github.com/JetBrains/JetBrainsRuntime). The installed JDK's release metadata records source revision `c628622cebf8` and vendor build details. This repository does not automatically download or install a host JDK.

Run:

```sh
export JAVA_HOME=/absolute/path/to/pinned/jbr-sdk/Contents/Home
./scripts/package-macos.sh
```

The script builds the two workers and Maven metadata extension, publishes the server, gathers notices, and records runtime versions and file hashes. It downloads the official macOS arm64 Node archive and verifies its pinned SHA-256 before extracting the runtime. Set `CQC_NODE_ARCHIVE` to use a previously downloaded archive with the same checksum. The MCP smoke test runs with only Git on `PATH`, exercises Java, C#, and TypeScript, and checks React complexity, a resolved module cycle, bounded messages, raw pagination, and unchanged inputs.

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
