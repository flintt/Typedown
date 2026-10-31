#!/usr/bin/env python3
"""Every icon the app draws from the system's icon font is in Windows 10's too.

Windows 11 draws icons with Segoe Fluent Icons, Windows 10 with Segoe MDL2 Assets, which has fewer: a code point only the
first has showed nothing on Windows 10 (the Mica row in the settings, 2026-10-30). Looked for in the app's XAML and C#
(&#xE8A5;, \\uE8A5, the character itself) and compared with windows10-glyphs.txt beside this script.

    python3 Tools/Icons/check-glyphs.py [folder]     default: Dev
"""
import os
import re
import sys

here = os.path.dirname(os.path.abspath(__file__))
root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, '..', '..', 'Dev')
have = {int(line, 16) for line in open(os.path.join(here, 'windows10-glyphs.txt'), encoding='utf-8') if line.strip() and not line.startswith('#')}
missing = []
for folder, dirs, files in os.walk(root):
    dirs[:] = [d for d in dirs if d not in ('node_modules', 'bin', 'obj', 'Statics', 'Typedown.Editor', '.git')]
    for name in files:
        if not name.endswith(('.xaml', '.cs')):
            continue
        path = os.path.join(folder, name)
        for number, line in enumerate(open(path, encoding='utf-8-sig', errors='replace'), 1):
            points = [int(m, 16) for m in re.findall(r'&#x([0-9A-Fa-f]{4,5});', line)]
            points += [int(m, 16) for m in re.findall(r'\\u([EeFf][0-9A-Fa-f]{3})', line)]
            points += [ord(ch) for ch in line if 0xE000 <= ord(ch) <= 0xF8FF]
            for point in points:
                if 0xE000 <= point <= 0xF8FF and point not in have:
                    missing.append(f'{os.path.relpath(path, root)}:{number}: U+{point:04X} is not in Windows 10\'s icon font')
for line in missing:
    print(line)
sys.exit(1 if missing else 0)
