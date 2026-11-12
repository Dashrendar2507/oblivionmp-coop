"""Fails when the game's own files would be published: in the repository, or in a release zip.

    python3 -I tools/check_no_game_files.py              # every file git tracks
    python3 -I tools/check_no_game_files.py some.zip     # the files in a zip, before uploading it as a release

The game's text (strings.json, *.locres) and plugins (Oblivion.esm, the DLC and remaster .esp files) belong to
Bethesda. Server hosts make strings.json from their own copy of the game (docs/INSTALL.md); it is never shipped.
"""
import fnmatch, os, subprocess, sys, zipfile

BLOCKED = [
    "strings.json", "*strings.json", "coop-quest-sync-strings*.json", "*.locres", "*.esm", "*.bsa", "*.pak", "*.utoc", "*.ucas",
    "dlc*.esp", "knights.esp", "altar*.esp", "tamrielleveledregion.esp",
]


def blocked(path):
    name = os.path.basename(path.replace("\\", "/")).lower()
    return any(fnmatch.fnmatch(name, pattern) for pattern in BLOCKED)


def main(args):
    if args:
        names = [n for zip_path in args for n in zipfile.ZipFile(zip_path).namelist()]
        where = ", ".join(args)
    else:
        names = subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True).stdout.decode().split("\0")
        where = "the repository"
    bad = sorted(n for n in names if n and blocked(n))
    if bad:
        print(f"Game files found in {where}. They must never be published:")
        for n in bad:
            print("  " + n)
        return 1
    print(f"No game files in {where}.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
