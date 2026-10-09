#!/usr/bin/env python3
"""
Builds the Ubuntu Touch package: publishes the Avalonia desktop head for linux-arm64 and wraps it
in a .click, without clickable or a container.

    python packaging/ubuntu-touch/build-click.py            -> artifacts/gw2companion.mpsoft_<version>_arm64.click
    python packaging/ubuntu-touch/build-click.py --no-publish   (reuse the last publish)

Install on the phone with
    adb push <file>.click /home/phablet/Downloads/ && adb shell pkcon install-local --allow-untrusted /home/phablet/Downloads/<file>.click

Why not clickable: it needs Docker or Podman on a Linux host, and a .click is only an ar archive
of two tarballs and two marker files. What clickable adds on top is the review (click-review),
which matters for the OpenStore and not for sideloading; run it there when the time comes.

The archive is built here rather than with tar/ar because this runs on Windows, where files carry
no Unix owner or mode: everything is written as root:root, and the files that must run get 0755.
"""

import argparse
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tarfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
PROJECT = ROOT / "GuildWarsCompanion.Avalonia.Desktop" / "GuildWarsCompanion.Avalonia.Desktop.csproj"
ICON = ROOT / "GuildWarsCompanion.Avalonia.Android" / "Resources" / "mipmap-xxxhdpi" / "appicon.png"
ARTIFACTS = ROOT / "artifacts"

RID, ARCH = "linux-arm64", "arm64"

# Everything else in the package is data; these are the files that are started.
EXECUTABLES = {"launcher.sh", "app/GuildWarsCompanion.Desktop", "app/createdump"}

PREINST = """#! /bin/sh
echo "Click packages may not be installed directly using dpkg."
echo "Use 'click install' instead."
exit 1
"""


def git(*args: str) -> str | None:
    try:
        return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def version() -> str:
    """The repository's own version, minus the build metadata: click versions are Debian versions,
    and the commit hash after '+' would be allowed there but sorts in surprising ways. The commit
    count already orders builds, as it does for Android's versionCode."""
    count = git("rev-list", "--count", "HEAD")
    return f"1.0.{count}" if count else "1.0.0"


def maintainer() -> str:
    name = git("config", "user.name") or "unknown"
    email = git("config", "user.email") or "unknown@invalid"
    return f"{name} <{email}>"


def publish(out: Path) -> None:
    if out.exists():
        shutil.rmtree(out)
    subprocess.run(
        ["dotnet", "publish", str(PROJECT), "-r", RID, "--self-contained", "true", "-c", "Release", "-o", str(out), "-nologo", "-v", "q"],
        check=True)


def stage(publish_dir: Path, stage_dir: Path, manifest: dict) -> None:
    if stage_dir.exists():
        shutil.rmtree(stage_dir)
    stage_dir.mkdir(parents=True)

    shutil.copytree(publish_dir, stage_dir / "app")

    # Unix line endings whatever the checkout did to them: /bin/sh reads "sh\r" otherwise, and the
    # desktop entry and AppArmor parsers are no more forgiving.
    for name in ("gw2companion.apparmor", "gw2companion.desktop", "launcher.sh"):
        (stage_dir / name).write_bytes((HERE / name).read_bytes().replace(b"\r\n", b"\n"))

    shutil.copy2(ICON, stage_dir / "icon.png")
    (stage_dir / "manifest.json").write_text(json.dumps(manifest, indent=4) + "\n", encoding="utf-8", newline="\n")


def files(stage_dir: Path) -> list[Path]:
    return sorted(p for p in stage_dir.rglob("*") if p.is_file())


def tar_bytes(entries: list[tuple[str, bytes, int]], directories: list[str]) -> bytes:
    buffer = io.BytesIO()
    now = int(time.time())
    with tarfile.open(fileobj=buffer, mode="w:gz", format=tarfile.GNU_FORMAT) as tar:
        for name in directories:
            info = tarfile.TarInfo(name)
            info.type, info.mode, info.mtime = tarfile.DIRTYPE, 0o755, now
            info.uid = info.gid = 0
            info.uname = info.gname = "root"
            tar.addfile(info)
        for name, data, mode in entries:
            info = tarfile.TarInfo(name)
            info.size, info.mode, info.mtime = len(data), mode, now
            info.uid = info.gid = 0
            info.uname = info.gname = "root"
            tar.addfile(info, io.BytesIO(data))
    return buffer.getvalue()


def ar(members: list[tuple[str, bytes]]) -> bytes:
    out = bytearray(b"!<arch>\n")
    now = int(time.time())
    for name, data in members:
        header = f"{name + '/':<16}{now:<12}{0:<6}{0:<6}{'100644':<8}{len(data):<10}`\n"
        out += header.encode("ascii") + data
        if len(data) % 2:
            out += b"\n"
    return bytes(out)


def build(stage_dir: Path, manifest: dict, out: Path) -> None:
    paths = files(stage_dir)
    rel = [p.relative_to(stage_dir).as_posix() for p in paths]
    data = {r: p.read_bytes() for r, p in zip(rel, paths)}

    directories = sorted({"./"} | {
        "./" + "/".join(r.split("/")[:i]) + "/" for r in rel for i in range(1, r.count("/") + 1)})

    data_tar = tar_bytes(
        [("./" + r, data[r], 0o755 if r in EXECUTABLES else 0o644) for r in rel], directories)

    installed_kib = (sum(len(b) for b in data.values()) + 1023) // 1024
    control_manifest = dict(manifest, **{"installed-size": str(installed_kib)})

    control = (
        f"Package: {manifest['name']}\n"
        f"Version: {manifest['version']}\n"
        f"Click-Version: 0.4\n"
        f"Architecture: {manifest['architecture']}\n"
        f"Maintainer: {manifest['maintainer']}\n"
        f"Installed-Size: {installed_kib}\n"
        f"Description: {manifest['title']}\n")

    md5sums = "".join(f"{hashlib.md5(data[r]).hexdigest()}  {r}\n" for r in rel)

    control_tar = tar_bytes([
        ("./control", control.encode(), 0o644),
        ("./manifest", (json.dumps(control_manifest, indent=4) + "\n").encode(), 0o644),
        ("./md5sums", md5sums.encode(), 0o644),
        ("./preinst", PREINST.encode(), 0o755),
    ], ["./"])

    out.write_bytes(ar([
        ("debian-binary", b"2.0\n"),
        ("_click-binary", b"0.4\n"),
        ("control.tar.gz", control_tar),
        ("data.tar.gz", data_tar),
    ]))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--no-publish", action="store_true", help="reuse artifacts/publish-linux-arm64")
    args = parser.parse_args()

    publish_dir = ARTIFACTS / f"publish-{RID}"
    stage_dir = ARTIFACTS / "click-stage"

    if not args.no_publish:
        publish(publish_dir)

    template = (HERE / "manifest.json").read_text(encoding="utf-8")
    manifest = json.loads(template
                          .replace("@ARCH@", ARCH)
                          .replace("@VERSION@", version())
                          .replace("@MAINTAINER@", maintainer()))

    stage(publish_dir, stage_dir, manifest)

    out = ARTIFACTS / f"{manifest['name']}_{manifest['version']}_{ARCH}.click"
    build(stage_dir, manifest, out)

    print(f"{out}  ({out.stat().st_size / 1024 / 1024:.1f} MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
