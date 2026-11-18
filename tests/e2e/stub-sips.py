#!/usr/bin/env python3
"""Stand-in for macOS sips on the stub Mac: `sips [-s k v] [-Z n] SRC --out DST` copies SRC to DST."""
import shutil, sys
a = sys.argv[1:]
out = a[a.index("--out") + 1]
skip = {"-s": 2, "-Z": 1, "--out": 1}
i, src = 0, None
while i < len(a):
    if a[i] in skip: i += skip[a[i]] + 1
    else: src = a[i]; i += 1
shutil.copyfile(src, out)
