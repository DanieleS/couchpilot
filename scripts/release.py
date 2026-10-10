"""Works out the next release from what was merged since the last tag, and stamps a version.

  python scripts/release.py plan  --repo owner/name --notes release-notes.md
  python scripts/release.py apply 1.2.3

`plan` looks at every commit since the newest `v*` tag reachable from HEAD, finds the pull
request each came from, and picks the bump from their labels: `release: major`, `release: minor`,
`release: patch` (the default, also for a commit pushed straight to main) or `release: skip`
(contributes nothing; if every change is skipped there's no release). A version already bumped by
hand in extension.yaml, above the last tag, wins. The notes come from each pull request's
`## Release notes` section, merged by heading (## Fixed, ## Changed…); one without the section
shows up as its title. Prints `release=true|false` and `version=x.y.z`, for $GITHUB_OUTPUT.

`apply` writes the version into extension.yaml and CouchPilot.csproj, which must agree with the tag.
"""

import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
YAML = ROOT / "src/CouchPilot/extension.yaml"
CSPROJ = ROOT / "src/CouchPilot/CouchPilot.csproj"

BUMPS = {"release: skip": 0, "release: patch": 1, "release: minor": 2, "release: major": 3}
OTHER = "Other changes"


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True, encoding="utf-8").stdout.strip()


def parse(version):
    return tuple(int(part) for part in version.split("."))


def bump(version, level):
    major, minor, patch = parse(version)
    if level == 3:
        return f"{major + 1}.0.0"
    if level == 2:
        return f"{major}.{minor + 1}.0"
    return f"{major}.{minor}.{patch + 1}"


def file_version():
    return re.search(r"^Version:\s*(.+)$", YAML.read_text(encoding="utf-8"), re.M).group(1).strip()


def notes_section(body):
    """The lines under a `## Release notes` heading, up to the next heading of that level or above."""
    lines = (body or "").replace("\r\n", "\n").split("\n")
    out, inside = [], False
    for line in lines:
        heading = re.match(r"^(#{1,2})\s+(.*?)\s*$", line)
        if heading and heading.group(2).lower().rstrip(":") == "release notes":
            inside = True
            continue
        if inside and heading:
            break
        if inside:
            out.append(line)
    return "\n".join(out).strip()


def merge_notes(chunks, fallbacks):
    """Joins the notes of several pull requests, one `## Heading` each, in the order first seen."""
    groups = {}
    for chunk in chunks:
        # Each piece is (heading, text) for one pull request; pieces under one heading are
        # stacked without a blank line, so their bullets stay one list.
        heading, lines = "", []
        for line in chunk.split("\n") + ["## "]:
            match = re.match(r"^#{2,4}\s+(.*?)\s*$", line)
            if not match:
                lines.append(line)
                continue
            text = re.sub(r"\n{3,}", "\n\n", "\n".join(lines).strip())
            if text:
                groups.setdefault(heading, []).append(text)
            heading, lines = match.group(1), []
    if fallbacks:
        groups.setdefault(OTHER if any(groups) else "", []).append("\n".join(fallbacks))

    parts = []
    for heading, texts in groups.items():
        text = "\n".join(texts)
        parts.append(f"## {heading}\n\n{text}" if heading else text)
    return "\n\n".join(parts)


def plan(repo, notes_path):
    try:
        tag = run("git", "describe", "--tags", "--abbrev=0", "--match", "v*")
        last, commits = tag[1:], run("git", "log", "--no-merges", "--format=%H %ae %s", f"{tag}..HEAD")
    except subprocess.CalledProcessError:
        last, commits = "0.0.0", run("git", "log", "--no-merges", "--format=%H %ae %s")

    level, pulls, loose = 0, {}, []
    for line in filter(None, commits.split("\n")):
        sha, email, subject = line.split(" ", 2)
        # The release commits this workflow makes.
        if email.startswith("41898282+github-actions[bot]"):
            continue
        found = json.loads(run("gh", "api", f"repos/{repo}/commits/{sha}/pulls"))
        merged = [p for p in found if p.get("merged_at") and p["base"]["ref"] == "main"]
        if not merged:
            level = max(level, 1)
            loose.append(f"- {subject}")
        for pull in merged:
            pulls[pull["number"]] = pull

    chunks, fallbacks = [], []
    for number in sorted(pulls):
        pull = pulls[number]
        labels = [label["name"].lower() for label in pull["labels"]]
        levels = [BUMPS[name] for name in labels if name in BUMPS]
        if levels and max(levels) == 0:
            continue
        level = max(level, max(levels) if levels else 1)
        notes = notes_section(pull.get("body"))
        if notes:
            chunks.append(notes)
        else:
            fallbacks.append(f"- {pull['title']} (#{number})")
    fallbacks += loose

    if level == 0:
        print("release=false")
        print(f"version={last}")
        return

    # A version bumped by hand in the pull request goes out as it is.
    version = file_version()
    if parse(version) <= parse(last):
        version = bump(last, level)

    body = merge_notes(chunks, fallbacks)
    body += f"\n\n**Install:** download `CouchPilot_{version}.pext` and double-click it with Playnite open.\n"
    Path(notes_path).write_text(body.lstrip(), encoding="utf-8")
    print("release=true")
    print(f"version={version}")


def stamp(path, pattern, replacement):
    # newline="" both ways, so the file keeps its line endings.
    with open(path, encoding="utf-8", newline="") as f:
        text = f.read()
    text, count = re.subn(pattern, replacement, text, count=1, flags=re.M)
    if count != 1:
        sys.exit(f"No version in {path}")
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(text)


def apply(version):
    parse(version)
    stamp(YAML, r"^Version:[^\r\n]*", f"Version: {version}")
    stamp(CSPROJ, r"<Version>.*?</Version>", f"<Version>{version}</Version>")


if __name__ == "__main__":
    if sys.argv[1:2] == ["plan"]:
        args = dict(zip(sys.argv[2::2], sys.argv[3::2]))
        plan(args["--repo"], args["--notes"])
    elif sys.argv[1:2] == ["apply"]:
        apply(sys.argv[2])
    else:
        sys.exit(__doc__)
