#!/bin/bash
set -euo pipefail

fail() {
    printf 'CQC installer: %s\n' "$1" >&2
    exit 1
}

select_clients() {
    local selection
    local first second extra
    printf 'Install CQC for which clients?\n  1) Claude Code\n  2) Codex\n' >&2
    while true; do
        printf 'Enter 1, 2, or both (1 2). Press Enter to cancel: ' >&2
        if ! IFS= read -r selection; then
            return 1
        fi
        read -r first second extra <<< "${selection//,/ }"
        case "$first $second $extra" in
            '  ') return 1 ;;
            '1  ') printf 'claude\n'; return ;;
            '2  ') printf 'codex\n'; return ;;
            '1 2 '|'2 1 ') printf 'both\n'; return ;;
            *) printf 'Choose 1, 2, or both.\n' >&2 ;;
        esac
    done
}

cleanup() {
    if [[ -d "$staging/previous" && ! -e "$destination" ]]; then
        if ! mv "$staging/previous" "$destination"; then
            printf 'CQC installer: Previous release retained at %s\n' "$staging/previous" >&2
            return
        fi
    fi
    rm -rf -- "$staging"
}

prepare_standalone_installer() {
    local installers="$root/installers"
    local staged_installer="$staging/installer"
    local source="https://raw.githubusercontent.com/ZsoltGajdacs/CodeQualityChecker/$tag"
    local project_skill=plugins/skills/check-project/SKILL.md
    local legacy_skill=plugins/skills/cqc-review/SKILL.md
    local status file
    local files=(
        scripts/install-plugin.sh
        scripts/install-plugin.py
        scripts/package-plugins.py
        plugins/claude/.claude-plugin/plugin.json
        plugins/codex/.codex-plugin/plugin.json
    )

    installer_root="$installers/$tag"
    [[ ! -L "$installers" && ! -L "$installer_root" ]] || fail 'The plugin installer path contains a symbolic link.'
    # The launcher on master can run before a release with the split skills is published.
    if [[ -d "$installer_root" ]]; then
        [[ -f "$installer_root/$legacy_skill" ]] && status=404 || status=200
    else
        status=$(curl "${curl_options[@]}" --silent --no-show-error --head \
            --output /dev/null --write-out '%{http_code}' "$source/$project_skill") ||
            [[ "$status" == 404 ]] || fail "Could not locate $project_skill in $tag."
    fi
    if [[ "$status" == 404 ]]; then
        files+=("$legacy_skill")
    else
        files+=("$project_skill" plugins/skills/check-uncommitted/SKILL.md plugins/skills/review-report.md)
    fi
    if [[ -d "$installer_root" ]]; then
        for file in "${files[@]}"; do
            [[ -s "$installer_root/$file" ]] || fail "Missing plugin installer file: $installer_root/$file"
        done
        [[ -x "$installer_root/scripts/install-plugin.sh" ]] || fail 'The cached plugin installer is not executable.'
        return
    fi
    [[ ! -e "$installer_root" ]] || fail "Expected a directory at $installer_root"
    for file in "${files[@]}"; do
        mkdir -p -- "$staged_installer/${file%/*}"
        curl "${curl_options[@]}" --output "$staged_installer/$file" "$source/$file" ||
            fail "Could not download $file from $tag."
        [[ -s "$staged_installer/$file" ]] || fail "Empty plugin installer file from $tag: $file"
    done
    chmod +x "$staged_installer/scripts/install-plugin.sh"
    mkdir -p -- "$installers"
    mv "$staged_installer" "$installer_root"
}

if [[ $# -eq 1 && $1 == --standalone ]]; then
    standalone=true
elif [[ $# -eq 0 ]]; then
    standalone=false
else
    fail 'Usage: install.sh [--standalone]'
fi

if ! target=$(select_clients); then
    printf 'Installation canceled.\n'
    exit 0
fi

[[ $(uname -sm) == 'Darwin arm64' ]] || fail 'The published release requires macOS on Apple Silicon.'
if [[ $target == both ]]; then
    clients=(claude codex)
else
    clients=("$target")
fi
for command in "${clients[@]}" curl python3 shasum tar; do
    command -v "$command" >/dev/null || fail "Required command not found: $command"
done
python3 -c 'import sys; sys.exit(sys.version_info < (3, 9))' || fail 'Python 3.9 or later is required.'

if "$standalone"; then
    home=$(CDPATH='' cd -- "$HOME" && pwd -P)
    application_support="$home/Library/Application Support"
    root="$application_support/CodeQualityChecker"
    [[ ! -L "$home/Library" && ! -L "$application_support" && ! -L "$root" ]] ||
        fail 'The application support path contains a symbolic link.'
else
    root=$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
fi
installer_root="$root"
package=cqc-osx-arm64
release_dir="$root/dist/release"
destination="$release_dir/$package"
[[ ! -L "$root/dist" && ! -L "$release_dir" && ! -L "$destination" ]] || fail 'The release install path contains a symbolic link.'
[[ ! -e "$destination" || -d "$destination" ]] || fail "Expected a directory at $destination"

mkdir -p -- "$release_dir"
staging="$release_dir/.installing"
mkdir -- "$staging" || fail "Could not reserve $staging. Another installer may be running."
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

releases='https://github.com/ZsoltGajdacs/CodeQualityChecker/releases'
curl_options=(--fail --silent --show-error --location --proto '=https' --proto-redir '=https' --retry 2)
latest=$(curl "${curl_options[@]}" --output /dev/null --write-out '%{url_effective}' "$releases/latest") ||
    fail 'Could not resolve the latest GitHub release.'
tag=${latest#"$releases/tag/"}
[[ "$latest" == "$releases/tag/$tag" && "$tag" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] ||
    fail 'GitHub did not return a versioned release.'

archive="$package.tar.gz"
printf 'Downloading CQC %s for macOS Apple Silicon.\n' "$tag"
for asset in "$archive" "$archive.sha256"; do
    curl "${curl_options[@]}" --output "$staging/$asset" "$releases/download/$tag/$asset" ||
        fail "Could not download $asset from $tag."
done

checksum=$(shasum -a 256 "$staging/$archive")
read -r expected _ < "$staging/$archive.sha256" || fail 'Could not read the release checksum.'
[[ "$expected" =~ ^[[:xdigit:]]{64}$ && "${checksum%% *}" == "$expected" ]] ||
    fail 'The archive checksum does not match the release checksum.'
tar -xzf "$staging/$archive" -C "$staging" || fail 'Could not extract the release archive.'
[[ -f "$staging/$package/cqc" && -x "$staging/$package/cqc" && ! -L "$staging/$package/cqc" ]] ||
    fail 'The release archive is missing the CQC executable.'

if "$standalone"; then
    prepare_standalone_installer
fi
if [[ -d "$destination" ]]; then
    mv "$destination" "$staging/previous"
fi
mv "$staging/$package" "$destination"
"$installer_root/scripts/install-plugin.sh" "$target" --mcp-dir "$destination"
