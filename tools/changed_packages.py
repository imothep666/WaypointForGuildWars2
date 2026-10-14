#!/usr/bin/env python3
"""Which of the release's packages changed since the last release, for azure-pipelines.yml.

A package is rebuilt only when something it is made from changed since the commit the last
release was built from. What it is made from is its project's own folder, and every file and
project the project reaches by a path out of that folder ("..\\GuildWarsCompanion.Core\\..."),
followed through the projects it references. The bridge compiles a few of Core's and the desktop
app's files by link, so "nothing in the Bridge folder changed" is not enough; reading the project
files keeps that list right without anyone keeping it by hand. A few files shape every build
(Directory.Build.*, global.json, NuGet's config), and the package's icon is packed in too. The
pipeline file is not among them: most of its changes are to how a release is made rather than to
what a package holds, and a run started by hand with "Build every package" covers the rest.

Velopack reads each channel's feed from all recent releases, not only the newest, so a release
without a package leaves that package's installs on its last release, still found.

Reads PACKAGES (the pipeline's packages parameter as JSON), LAST_RELEASE (a commit, or empty) and
EVERYTHING ("True" to build every package regardless) from the environment. Prints, for each
unchanged package, the pipeline command that skips it, and marks the run as released already when
nothing changed at all.

Run locally:  PACKAGES="$(cat packages.json)" LAST_RELEASE=<sha> python3 tools/changed_packages.py
"""

import json
import os
import posixpath
import re
import subprocess
import sys

SHARED = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
          "global.json", "NuGet.config", "nuget.config"]


def git(*args):
    return subprocess.run(["git", *args], capture_output=True, text=True)


def inputs(project, seen=None):
    """Path prefixes a project is made from: folders end in "/", files do not."""
    seen = seen if seen is not None else set()
    if project in seen:
        return set()
    seen.add(project)

    folder = posixpath.dirname(project)
    found = {folder + "/"}

    with open(project, encoding="utf-8-sig") as file:
        text = file.read()

    for rel in re.findall(r'Include="(\.\.[^"]*)"', text):
        rel = rel.replace("\\", "/")
        # Cut at anything MSBuild would expand: a property or a wildcard.
        rel = re.split(r"[$*]", rel, maxsplit=1)[0]
        target = posixpath.normpath(posixpath.join(folder, rel))

        if target.endswith(".csproj"):
            found |= inputs(target, seen)
        elif rel.endswith("/"):
            found.add(target + "/")
        else:
            found.add(target)

    return found


def touches(changed, prefixes):
    return [path for path in changed
            if any(path == prefix or (prefix.endswith("/") and path.startswith(prefix)) for prefix in prefixes)]


def main():
    packages = json.loads(os.environ["PACKAGES"])
    last = os.environ.get("LAST_RELEASE", "").strip()
    if last.startswith("$("):   # an unset pipeline variable arrives as its own name
        last = ""

    if os.environ.get("EVERYTHING", "").lower() == "true":
        print("Asked to build every package.")
        return

    if not last or git("merge-base", "--is-ancestor", last, "HEAD").returncode != 0:
        print("No released commit to compare with: every package is built.")
        return

    changed = git("diff", "--name-only", last, "HEAD").stdout.split()
    print(f"{len(changed)} files changed since {last[:7]}.")

    built = 0
    for package in packages:
        prefixes = inputs(package["project"]) | set(SHARED) | {package["icon"]}
        hits = touches(changed, prefixes)

        if hits:
            built += 1
            print(f"{package['channel']}: built, for {', '.join(hits[:5])}"
                  + (f" and {len(hits) - 5} more" if len(hits) > 5 else ""))
        else:
            print(f"{package['channel']}: unchanged, skipped")
            print(f"##vso[task.setvariable variable=Skip_{package['channel'].replace('-', '_')}]true")

    if built == 0:
        print("Nothing in any package changed since the last release; nothing to release.")
        print("##vso[task.setvariable variable=Released]true")


if __name__ == "__main__":
    sys.exit(main())
