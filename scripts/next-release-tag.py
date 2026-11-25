#!/usr/bin/env python3
"""Choose a release tag for the checked-out master push."""

import re
import subprocess
import sys

STABLE_TAG = re.compile(r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
COMMIT_HEADER = re.compile(r"([a-zA-Z]+)(?:\([^()\n]+\))?(!)?: ")
FOOTER_START = re.compile(r"^(?:BREAKING CHANGE: |[a-zA-Z0-9-]+(?:: | #))")
BREAKING_FOOTER = re.compile(r"^BREAKING[ -]CHANGE: \S", re.MULTILINE)


def git(*args):
    return subprocess.check_output(["git", *args], text=True)


def stable_tags(names):
    tags = {}
    for tag in names:
        match = STABLE_TAG.fullmatch(tag)
        if match:
            tags[tuple(map(int, match.groups()))] = tag
    return tags


def has_breaking_footer(body):
    for paragraph in body.strip().split("\n\n"):
        if FOOTER_START.match(paragraph) and BREAKING_FOOTER.search(paragraph):
            return True
    return False


def bump_version(version, messages):
    major, minor, patch = version
    has_feature = False
    for message in messages.split("\0"):
        subject, _, remainder = message.partition("\n")
        header = COMMIT_HEADER.match(subject)
        breaking_header = header is not None and header[2] is not None
        breaking_footer = has_breaking_footer(remainder)
        if breaking_header or breaking_footer:
            return major + 1, 0, 0
        if header and header[1].lower() == "feat":
            has_feature = True
    if has_feature:
        return major, minor + 1, 0
    return major, minor, patch + 1


def choose_tag():
    tags = stable_tags(git("tag").splitlines())
    if not tags:
        return "v0.3.0"

    highest_version = max(tags)
    latest_tag = tags[highest_version]
    existing = stable_tags(git("tag", "--points-at", "HEAD").splitlines())
    if existing:
        return existing[max(existing)]

    if latest_tag in git("tag", "--contains", "HEAD").splitlines():
        print(f"Push is already included in {latest_tag}.", file=sys.stderr)
        return None
    if latest_tag not in git("tag", "--merged", "HEAD").splitlines():
        raise SystemExit(f"Latest release tag {latest_tag} is outside this push's history.")

    messages = git("log", "-z", "--format=%B", f"{latest_tag}..HEAD", "--")
    major, minor, patch = bump_version(highest_version, messages)
    return f"v{major}.{minor}.{patch}"


if __name__ == "__main__":
    tag = choose_tag()
    if tag:
        published = sys.argv[1].splitlines() if len(sys.argv) > 1 else []
        candidates = stable_tags([tag, *published])
        latest = candidates[max(candidates)] == tag
        print(f"tag={tag}")
        print(f"latest={str(latest).lower()}")
