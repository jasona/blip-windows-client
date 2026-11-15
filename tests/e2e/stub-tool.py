#!/usr/bin/env python3
"""Stand-in for imsg-send / imsg-read on the stub Mac: no AppleScript off a Mac.
Records argv and the exact stdin bytes so tests can prove text never rode argv."""
import json, os, sys
data = sys.stdin.buffer.read()
with open(os.path.expanduser("~/sent.log"), "a") as f:
    f.write(json.dumps({"tool": os.path.basename(sys.argv[0]), "argv": sys.argv[1:], "stdin_hex": data.hex()}) + "\n")
print("sent")
