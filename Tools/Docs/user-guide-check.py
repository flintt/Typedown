#!/usr/bin/env python3
"""Checks that the English user guide (docs/user-guide.en.md) keeps the Chinese one's structure (docs/user-guide.md):
the same headings, level for level and in the same order, outside code blocks. Help > User guide opens one or the
other by the interface language, so a section added to one guide and not the other goes missing for half the readers.

    python3 Tools/Docs/user-guide-check.py
"""
import os, re, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')

def levels(name):
    out, fence = [], False
    for line in open(os.path.join(ROOT, 'docs', name), encoding='utf-8'):
        if line.lstrip().startswith('```'):
            fence = not fence
            continue
        m = None if fence else re.match(r'(#{1,6})\s+(.*)', line)
        if m:
            out.append((len(m.group(1)), m.group(2).strip()))
    return out

# An edition's supplement (added at the end of the guide it opens) is held to the same rule, in pairs.
pairs = [('user-guide.md', 'user-guide.en.md')]
if any(os.path.exists(os.path.join(ROOT, 'docs', n)) for n in ('user-guide.edition.md', 'user-guide.edition.en.md')):
    pairs.append(('user-guide.edition.md', 'user-guide.edition.en.md'))
failed = False
for zh_name, en_name in pairs:
    for name in (zh_name, en_name):
        if not os.path.exists(os.path.join(ROOT, 'docs', name)):
            print(f'FAIL docs/{name} is missing: the supplement comes in Chinese and English')
            sys.exit(1)
    zh, en = levels(zh_name), levels(en_name)
    if [l for l, _ in zh] != [l for l, _ in en]:
        print(f'FAIL docs/{en_name} does not have docs/{zh_name}\'s headings:')
        for i in range(max(len(zh), len(en))):
            a = zh[i] if i < len(zh) else (0, '-')
            b = en[i] if i < len(en) else (0, '-')
            mark = '  ' if a[0] == b[0] else '!!'
            print(f'{mark} {"#" * a[0]} {a[1]:<40} | {"#" * b[0]} {b[1]}')
        failed = True
    else:
        print(f'OK docs/{zh_name} and docs/{en_name} have the same {len(zh)} headings')
sys.exit(1 if failed else 0)
