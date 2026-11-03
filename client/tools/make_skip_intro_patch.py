"""Regenerates Resources/SkipIntroPatch.gz from a vanilla save made with no client attached, at the point Skip Intro
should start from: first control in the Embarkation Lobby after the credits, before talking to the DeskBot.

Usage: python make_skip_intro_patch.py "<game folder>" <save slot>

Prints the SHA-256 of newgame.st, which goes in SkipIntroSave.NewGameSha256.
"""
import gzip
import hashlib
import os
import re
import subprocess
import sys
import tempfile
import zlib

TREE_START = b'\n{\n\t"CProjectItem"'
HUNK = re.compile(rb"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")
OUTPUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Resources", "SkipIntroPatch.gz")


def diff_hunks(old, new):
    """(start, removed, added lines) for each change, with start counted from 0."""
    with tempfile.TemporaryDirectory() as folder:
        with open(os.path.join(folder, "a"), "wb") as f:
            f.write(old)
        with open(os.path.join(folder, "b"), "wb") as f:
            f.write(new)
        diff = subprocess.run(["git", "diff", "--no-index", "--text", "-U0", "--patience", "--no-color", "a", "b"],
                              cwd=folder, capture_output=True).stdout

    hunks = []
    for line in diff.split(b"\n"):
        match = HUNK.match(line)
        if match:
            start, removed = int(match.group(1)), int(match.group(2) or 1)
            # git numbers a pure insertion by the line before it
            hunks.append((start - 1 if removed else start, removed, []))
        elif hunks and line[:1] == b"+":
            hunks[-1][2].append(line[1:])
    return hunks


def apply_hunks(text, hunks):
    lines = text.split(b"\n")
    output = []
    position = 0
    for start, removed, added in hunks:
        output += lines[position:start] + added
        position = start + removed
    return b"\n".join(output + lines[position:])


def main():
    game_folder, slot = sys.argv[1], int(sys.argv[2])

    with open(os.path.join(game_folder, "newgame.st"), "rb") as f:
        new_game_file = f.read()
    with open(os.path.join(game_folder, "Saves", f"titanic-win.{slot:03d}"), "rb") as f:
        capture = gzip.decompress(f.read())

    new_game = zlib.decompress(new_game_file)
    capture_tree = capture[capture.find(TREE_START):]
    hunks = diff_hunks(new_game, capture_tree)
    if apply_hunks(new_game, hunks) != capture_tree:
        sys.exit("The patch doesn't reproduce the save")

    patch = b"\n".join(b"%d %d %d\n%s" % (start, removed, len(added), b"\n".join(added)) if added
                       else b"%d %d 0" % (start, removed) for start, removed, added in hunks)
    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "wb") as f:
        f.write(gzip.compress(patch, 9))

    print("newgame.st SHA-256:", hashlib.sha256(new_game_file).hexdigest().upper())
    print(f"{len(hunks)} hunks, {os.path.getsize(OUTPUT)} bytes")


main()
