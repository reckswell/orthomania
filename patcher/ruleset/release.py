"""The release workflow's steps (.github/workflows/lazer-builds.yml), runnable by hand too.

find     Which lazer releases to build for, and the build's number: the newest release on each of lazer's channels (lazer and
         tachyon) on ppy/osu's GitHub releases.
fetch    One lazer release's own files, from the package its updater installs (the same one orthomania.exe's update gets), and
         lazer.props, which builds the ruleset and its tests against exactly those files (-p:LazerProps=...). NuGet is no
         substitute: ppy does not publish every lazer release there.
package  orthomania.json (manifest format 2, which orthomania.exe reads) and the files it lists, ready to attach to the
         GitHub release lazer-<release>-<build>.

Only the standard library: it runs as is on the workflow's Windows and Linux machines.
"""

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
import xml.sax.saxutils
import zipfile

LAZER_REPOSITORY = "ppy/osu"
DOWNLOAD = "https://github.com/" + LAZER_REPOSITORY + "/releases/download/{tag}/{file}"

# lazer's updater's list of a release's packages (Velopack).
FEED = "releases.win.json"

# A lazer release as orthomania.exe names it: year.monthday.patch, nothing after it.
RELEASE = re.compile(r"^\d{4}\.\d{1,4}\.\d+$")

# ppy/osu's tags: the release, then its channel.
LAZER_TAG = re.compile(r"^(\d{4}\.\d{1,4}\.\d+)(?:-(lazer|tachyon))?$")

# Files of a lazer release the ruleset is never built against: the game's own entry point and the tournament client.
NOT_REFERENCED = {"osu!.dll", "osu.Game.Tournament.dll"}

# orthomania.exe's ReleaseManifest.IsSafeBuildName.
BUILD = re.compile(r"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,63}$")

TAG_PREFIX = "lazer-"
MANIFEST = "orthomania.json"
MANIFEST_FORMAT = 2

# Everything a build ships, beside lazer's own files (never one of lazer's, never osu.Game.Resources.dll).
SHIPPED = ["osu.Game.Rulesets.ManiaOrtho.dll", "osu.Game.Rulesets.ManiaOrtho.pdb", "0Harmony.dll"]


def release_key(version):
    return tuple(int(part) for part in version.split("."))


def lazer_releases():
    """ppy/osu's newest releases with an updater package, newest first: (release, tag, prerelease)."""
    output = subprocess.run(["gh", "api", f"repos/{LAZER_REPOSITORY}/releases?per_page=30", "--jq",
                             '.[] | select(.draft | not) | [.tag_name, (.prerelease | tostring), ([.assets[].name] | join(","))] | join(" ")'],
                            check=True, capture_output=True, text=True).stdout

    for line in output.splitlines():
        parts = line.split(" ")

        if len(parts) < 3 or FEED not in parts[2].split(","):
            continue

        match = LAZER_TAG.match(parts[0])

        if match:
            yield match.group(1), parts[0], parts[1] == "true"


def published_tags(repository):
    """This repository's release tags (drafts too: a draft for a release means one is on its way)."""
    output = subprocess.run(["gh", "api", "--paginate", f"repos/{repository}/releases?per_page=100", "--jq", ".[].tag_name"],
                            check=True, capture_output=True, text=True).stdout
    return [line.strip() for line in output.splitlines() if line.strip()]


def find(args):
    asked = (args.lazer or "").strip()
    releases = list(lazer_releases())

    if asked:
        wanted = [(r, t) for r, t, _ in releases if r == asked or t == asked]

        if not wanted:
            sys.exit(f'"{asked}" is not one of ppy/osu\'s newest releases with an updater package.')
    else:
        # The newest of each channel: the one lazer's updater installs on it. Older ones are replaced on each player's next launch.
        wanted = []

        for prerelease in (False, True):
            newest = next(((r, t) for r, t, p in releases if p == prerelease), None)

            if newest and newest not in wanted:
                wanted.append(newest)

    build = f"1.0.{args.run_number}"

    if not BUILD.match(build):
        sys.exit(f'"{build}" is not a build number orthomania.exe accepts.')

    tags = published_tags(args.repository)
    matrix = []

    for release, tag in wanted:
        built = sorted(t for t in tags if t.startswith(f"{TAG_PREFIX}{release}-"))

        # On the hourly check, a release with a build is done. A change to the ruleset, or a run by hand, builds again: the higher
        # build number replaces the older build on each player's next launch.
        if args.event == "schedule" and built:
            print(f"osu! {tag} already has {', '.join(built)}.")
            continue

        print(f"Building orthomania {build} for osu! {tag}.")
        matrix.append({"lazer": release, "tag": tag})

    output(matrix=json.dumps({"include": matrix}, separators=(",", ":")), build=build, any="true" if matrix else "false")


def download(url, path, expected_sha256=None, expected_size=None):
    digest = hashlib.sha256()
    size = 0

    with urllib.request.urlopen(url, timeout=120) as response, open(path, "wb") as file:
        for block in iter(lambda: response.read(1 << 20), b""):
            file.write(block)
            digest.update(block)
            size += len(block)

    if expected_size is not None and size != expected_size:
        sys.exit(f"{url} is {size} bytes, not the {expected_size} lazer's updater lists.")

    if expected_sha256 is not None and digest.hexdigest().lower() != expected_sha256.lower():
        sys.exit(f"{url} does not have the SHA-256 lazer's updater lists.")


def fetch(args):
    """The release's own files, as its updater installs them, and lazer.props to build against them."""
    match = LAZER_TAG.match(args.tag)

    if not match:
        sys.exit(f'"{args.tag}" is not a lazer release tag (such as 2026.1005.0-lazer).')

    release = match.group(1)
    os.makedirs(args.to, exist_ok=True)

    feed_path = os.path.join(args.to, FEED)
    download(DOWNLOAD.format(tag=args.tag, file=FEED), feed_path)

    with open(feed_path, encoding="utf-8-sig") as file:
        feed = json.load(file)

    full = [a for a in feed.get("Assets", []) if a.get("Type") == "Full" and str(a.get("Version", "")).startswith(release)]

    if len(full) != 1:
        sys.exit(f"{args.tag}'s {FEED} lists {len(full)} full packages for {release}, not one.")

    asset = full[0]
    package_path = os.path.join(args.to, "lazer-full.nupkg")
    print(f"Downloading {asset['FileName']} ({asset['Size']} bytes)")
    download(DOWNLOAD.format(tag=args.tag, file=asset["FileName"]), package_path, asset["SHA256"], asset["Size"])

    app = os.path.join(args.to, "app")
    shutil.rmtree(app, ignore_errors=True)

    with zipfile.ZipFile(package_path) as package:
        for entry in package.infolist():
            if not entry.filename.startswith("lib/app/") or entry.is_dir():
                continue

            relative = entry.filename[len("lib/app/"):]
            target = os.path.normpath(os.path.join(app, relative))

            # Never outside the folder, whatever the package names.
            if os.path.commonpath([os.path.abspath(app), os.path.abspath(target)]) != os.path.abspath(app):
                sys.exit(f"{entry.filename} would land outside {app}.")

            os.makedirs(os.path.dirname(target), exist_ok=True)

            with package.open(entry) as source, open(target, "wb") as destination:
                shutil.copyfileobj(source, destination)

    os.remove(package_path)

    references, natives = files_of(app)
    write_props(os.path.join(args.to, "lazer.props"), release, app, references, natives)
    print(f"osu! {args.tag}: {len(references)} assemblies to build against, {len(natives)} native libraries for the tests, in {app}")


def files_of(app):
    """From the release's own list of its files (osu!.deps.json): the assemblies of its libraries, and their native libraries.
    .NET itself (the runtime pack lazer ships with) is left out: the build has its own."""
    with open(os.path.join(app, "osu!.deps.json"), encoding="utf-8-sig") as file:
        deps = json.load(file)

    target = deps["targets"][deps["runtimeTarget"]["name"]]
    references, natives = [], []

    for library, contents in target.items():
        if deps["libraries"].get(library, {}).get("type") == "runtimepack":
            continue

        for path in contents.get("runtime", {}):
            name = os.path.basename(path)

            if name not in NOT_REFERENCED and name not in references and os.path.isfile(os.path.join(app, name)):
                references.append(name)

        for path in contents.get("native", {}):
            name = os.path.basename(path)

            if name.lower().endswith((".dll", ".so", ".dylib")) and name not in natives and os.path.isfile(os.path.join(app, name)):
                natives.append(name)

    for needed in ("osu.Game.dll", "osu.Game.Rulesets.Mania.dll", "osu.Framework.dll"):
        if needed not in references:
            sys.exit(f"The release has no {needed} to build against.")

    return sorted(references), sorted(natives)


def write_props(path, release, app, references, natives):
    def item(kind, name):
        return f'    <{kind} Include="{xml.sax.saxutils.escape(os.path.abspath(os.path.join(app, name)))}" />'

    lines = [
        "<!-- Written by release.py fetch: build against this lazer release's own files. -->",
        "<Project>",
        "  <PropertyGroup>",
        f"    <LazerVersion>{release}</LazerVersion>",
        "  </PropertyGroup>",
        "  <ItemGroup>",
        *(item("LazerReference", n) for n in references),
        *(item("LazerNative", n) for n in natives),
        "  </ItemGroup>",
        "</Project>",
        "",
    ]

    with open(path, "w", encoding="utf-8", newline="\n") as file:
        file.write("\n".join(lines))


def sha256(path):
    digest = hashlib.sha256()

    with open(path, "rb") as file:
        for block in iter(lambda: file.read(1 << 20), b""):
            digest.update(block)

    return digest.hexdigest()


def package(args):
    if not RELEASE.match(args.lazer):
        sys.exit(f'"{args.lazer}" is not a lazer release.')

    if not BUILD.match(args.build) or ".." in args.build:
        sys.exit(f'"{args.build}" is not a build number orthomania.exe accepts.')

    os.makedirs(args.to, exist_ok=True)
    files = []

    for name in SHIPPED:
        source = os.path.join(args.source, name)

        if not os.path.isfile(source):
            sys.exit(f"{source} is missing: build osu.Game.Rulesets.ManiaOrtho first.")

        target = os.path.join(args.to, name)
        shutil.copyfile(source, target)
        files.append({"name": name, "sha256": sha256(target), "size": os.path.getsize(target)})

    manifest = {
        "format": MANIFEST_FORMAT,
        "lazer": args.lazer,
        "build": args.build,
        "commit": args.commit or None,
        "files": files,
    }

    with open(os.path.join(args.to, MANIFEST), "w", encoding="utf-8", newline="\n") as file:
        json.dump({k: v for k, v in manifest.items() if v is not None}, file, indent=2)
        file.write("\n")

    tag = f"{TAG_PREFIX}{args.lazer}-{args.build}"
    print(f"{tag}: {MANIFEST} and {', '.join(f['name'] for f in files)} in {args.to}")
    output(tag=tag)


def output(**values):
    """For the workflow's later steps (GITHUB_OUTPUT), and the console."""
    path = os.environ.get("GITHUB_OUTPUT")

    for key, value in values.items():
        print(f"{key}={value}")

        if path:
            with open(path, "a", encoding="utf-8") as file:
                file.write(f"{key}={value}\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    finding = commands.add_parser("find", help="which lazer releases to build for")
    finding.add_argument("--repository", required=True, help="owner/name, where the builds are published")
    finding.add_argument("--event", default="workflow_dispatch", help="what started the workflow")
    finding.add_argument("--run-number", required=True, help="the workflow's run number, the build's last part")
    finding.add_argument("--lazer", help="a release asked for by hand (2026.1005.0 or 2026.1005.0-lazer), instead of the newest")
    finding.set_defaults(run=find)

    fetching = commands.add_parser("fetch", help="one lazer release's own files, to build against")
    fetching.add_argument("--tag", required=True, help="ppy/osu's tag for the release, such as 2026.1005.0-lazer")
    fetching.add_argument("--to", required=True, help="an empty folder for its files")
    fetching.set_defaults(run=fetch)

    packaging = commands.add_parser("package", help="the manifest and files for one release")
    packaging.add_argument("--lazer", required=True)
    packaging.add_argument("--build", required=True)
    packaging.add_argument("--commit")
    packaging.add_argument("--source", required=True, help="the ruleset's build output")
    packaging.add_argument("--to", required=True, help="where the release's files go")
    packaging.set_defaults(run=package)

    args = parser.parse_args()
    args.run(args)


if __name__ == "__main__":
    main()
