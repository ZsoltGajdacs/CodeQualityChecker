#!/bin/bash
set -euo pipefail

task_root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$task_root"
if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
  echo 'Packaging requires macOS Apple Silicon.' >&2
  exit 1
fi
if [[ -z "${JAVA_HOME:-}" || ! -x "$JAVA_HOME/bin/jlink" ]]; then
  echo 'Set JAVA_HOME to the pinned JBR 25.0.2+10-b300.57 JDK; see docs/distribution.md.' >&2
  exit 1
fi
if ! task_dotnet_sdk="$(cd "$task_root/scripts" && dotnet --version)"; then
  echo 'Packaging requires .NET SDK 11.0.100 or a later patch in that feature band. See scripts/global.json.' >&2
  exit 1
fi
python3 - "$JAVA_HOME" <<'PY'
import hashlib, json, sys
from pathlib import Path
jdk = Path(sys.argv[1])
lock = json.loads(Path('upstream.lock.json').read_text())['analyzerRuntime']
expected = {'release': lock['releaseSha256']}
expected.update({f'jmods/{module}.jmod': sha for module, sha in lock['modules'].items()})
for relative, sha in expected.items():
    actual = hashlib.sha256((jdk / relative).read_bytes()).hexdigest()
    if actual != sha:
        raise SystemExit(f'Runtime input {relative} does not match upstream.lock.json')
PY

task_output="$task_root/dist/cqc-osx-arm64"
if [[ -L "$task_root/dist" || -L "$task_output" ]]; then
  echo 'Refusing symlinked distribution paths.' >&2
  exit 1
fi
mkdir -p "$task_root/dist"
rm -rf "$task_output"
task_scratch="$(mktemp -d "${TMPDIR:-/tmp/}cqc-package.XXXXXX")"
trap 'rm -rf "$task_scratch"' EXIT
cat > "$task_scratch/settings.xml" <<'XML'
<settings xmlns="http://maven.apache.org/SETTINGS/1.0.0"><mirrors><mirror><id>central</id><mirrorOf>*</mirrorOf><url>https://repo.maven.apache.org/maven2</url></mirror></mirrors></settings>
XML
mvn -B -s "$task_scratch/settings.xml" -f maven-metadata/pom.xml clean package
mvn -B -s "$task_scratch/settings.xml" -f java-worker/pom.xml clean package
mvn -B -s "$task_scratch/settings.xml" -f java-worker/pom.xml org.apache.maven.plugins:maven-dependency-plugin:3.9.0:copy-dependencies \
  -DincludeScope=runtime -Dmdep.copyPom=true "-DoutputDirectory=$task_root/java-worker/target/dependency"
(cd "$task_root/scripts" && dotnet publish "$task_root/src/Cqc.Server/Cqc.Server.csproj" \
  -c Release -r osx-arm64 --self-contained true -o "$task_output" -p:PublishSingleFile=false)
cp java-worker/target/java-worker.jar "$task_output/java-worker.jar"
cp maven-metadata/target/maven-metadata.jar "$task_output/maven-metadata.jar"
"$JAVA_HOME/bin/jlink" --module-path "$JAVA_HOME/jmods" \
  --add-modules java.se,jdk.compiler,jdk.unsupported \
  --strip-debug --no-header-files --no-man-pages --output "$task_output/runtime"
cp README.md THIRD_PARTY.md upstream.lock.json "$task_output/"
cp -R docs "$task_output/docs"
python3 - "$task_output" "$task_dotnet_sdk" <<'PY'
import hashlib, json, shutil, sys, zipfile
from pathlib import Path
output = Path(sys.argv[1])
assets = json.loads(Path('src/Cqc.Server/obj/project.assets.json').read_text())
runtime = json.loads((output / 'cqc.runtimeconfig.json').read_text())['runtimeOptions']
java_modules = next(line.split('=', 1)[1].strip('"').split() for line in (output / 'runtime/release').read_text().splitlines()
                    if line.startswith('MODULES='))
locked_modules = json.loads(Path('upstream.lock.json').read_text())['analyzerRuntime']['modules']
if set(java_modules) != set(locked_modules):
    raise SystemExit('Linked Java module set does not match upstream.lock.json')
for framework in runtime['includedFrameworks']:
    package = framework['name'].lower() + '.runtime.osx-arm64'
    source = next((Path(folder) / package / framework['version'] for folder in assets['packageFolders']
                   if (Path(folder) / package / framework['version']).is_dir()), None)
    if source is None:
        raise SystemExit('Cannot find bundled .NET runtime license source: ' + package)
    destination = output / 'legal' / 'dotnet' / (package + '-' + framework['version'])
    destination.mkdir(parents=True, exist_ok=True)
    for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
        shutil.copy2(source / name, destination / name)
notice_root = output / 'legal' / 'nuget'
for name, item in assets['libraries'].items():
    if item['type'] != 'package':
        continue
    for folder in assets['packageFolders']:
        source = Path(folder) / item['path']
        if not source.exists():
            continue
        destination = notice_root / name.replace('/', '-')
        destination.mkdir(parents=True, exist_ok=True)
        for path in source.rglob('*'):
            if path.is_file() and (path.suffix == '.nuspec' or path.name.lower().startswith(('license', 'notice', 'third-party', 'thirdpartynotice'))):
                target = destination / path.relative_to(source)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)
        break
for jar in sorted(Path('java-worker/target/dependency').glob('*.jar')):
    destination = output / 'legal' / 'java' / jar.stem
    with zipfile.ZipFile(jar) as archive:
        for name in archive.namelist():
            path = Path(name)
            if path.is_absolute() or '..' in path.parts:
                continue
            if path.name.lower().startswith(('license', 'notice', 'copying')) or path.name == 'pom.xml':
                target = destination / path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(archive.read(name))
for pom in sorted(Path('java-worker/target/dependency').glob('*.pom')):
    destination = output / 'legal' / 'java' / pom.stem
    destination.mkdir(parents=True, exist_ok=True)
    shutil.copy2(pom, destination / 'pom.xml')
manifest = {
    'schemaVersion': '1',
    'architecture': 'osx-arm64',
    'dotnetSdk': sys.argv[2],
    'javaRuntime': (output / 'runtime/release').read_text(),
    'files': {str(p.relative_to(output)): hashlib.sha256(p.read_bytes()).hexdigest()
              for p in sorted(output.rglob('*')) if p.is_file()}
}
(output / 'build-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
PY
python3 scripts/smoke-package.py "$task_output"
tar -czf "$task_root/dist/cqc-osx-arm64.tar.gz" -C "$task_root/dist" cqc-osx-arm64
(cd "$task_root/dist" && shasum -a 256 cqc-osx-arm64.tar.gz > cqc-osx-arm64.tar.gz.sha256)
echo "Package verified: $task_root/dist/cqc-osx-arm64.tar.gz"
