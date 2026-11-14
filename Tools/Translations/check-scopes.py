#!/usr/bin/env python3
"""No resource name is also the start of another one before a dot ("UploadImage" and "UploadImage.LocalOnly").

The Windows build's resource index takes the part before a dot as a scope, and a name both a string and a scope fails
it (PRI278, "defined as both resource and scope") - only on Windows, after everything else passed (CI, 2026-11-11).

    python3 Tools/Translations/check-scopes.py      # exit 1 naming each clash
"""
import glob
import os
import re
import sys

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')
bad = []
for path in sorted(glob.glob(os.path.join(root, 'Dev', '**', '*.resw'), recursive=True)):
    if os.sep + 'obj' + os.sep in path or os.sep + 'bin' + os.sep in path:
        continue
    # An edition's own texts (Dev/Edition) are read as XML beside the app, not through the resource index.
    if os.path.relpath(path, root).startswith(os.path.join('Dev', 'Edition') + os.sep):
        continue
    names = set(re.findall(r'<data name="([^"]+)"', open(path, encoding='utf-8-sig').read()))
    for name in names:
        parts = name.split('.')
        for i in range(1, len(parts)):
            scope = '.'.join(parts[:i])
            if scope in names:
                bad.append(f'{os.path.relpath(path, root)}: "{scope}" is a string and the scope of "{name}"')
for line in sorted(set(bad)):
    print(line)
sys.exit(1 if bad else 0)
