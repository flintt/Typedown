#!/usr/bin/env python3
"""Adds translated strings to the .resw files.

    python3 Tools/Translations/apply.py translations.json
    python3 Tools/Translations/apply.py --replace corrections.json

The JSON is {lang: {key: text}}; the file a key belongs to is taken from en, and keys that
the language already has are left alone (this never overwrites an existing translation).
With --replace it corrects keys the language has (a mistranslation), leaving their comments
and everything else in the file as it is; a key it does not have is added as without it.
"""
import json, os, re, sys
from xml.sax.saxutils import escape

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'Dev', 'Typedown.Core', 'Resources', 'Strings')
FILES = ['CommonResources.resw', 'DialogResources.resw', 'SettingsResources.resw', 'Resources.resw']

def strip_comments(text):
    """The .resw template carries sample entries (Name1, Bitmap1, Icon1) inside a comment."""
    return re.sub(r'<!--.*?-->', '', text, flags=re.S)



def keys_of(lang, name):
    path = os.path.join(ROOT, lang, name)
    if not os.path.exists(path):
        return set()
    return set(re.findall(r'<data name="([^"]+)"', strip_comments(open(path, encoding='utf-8').read())))


def replace(lang, strings, home):
    """Each key's <value> in the file it lives in, put right; the keys not there are returned."""
    left = dict(strings)
    for name in FILES:
        target = os.path.join(ROOT, lang, name)
        if not os.path.exists(target):
            continue
        text = open(target, encoding='utf-8').read()
        changed = 0
        for key, value in strings.items():
            if home.get(key) != name:
                continue
            pattern = re.compile(r'(<data name="' + re.escape(key) + r'"[^>]*>\s*<value>)(.*?)(</value>)', re.S)
            text, n = pattern.subn(lambda m: m.group(1) + escape(value) + m.group(3), text, count=1)
            if n:
                left.pop(key, None)
                changed += 1
        if changed:
            open(target, 'w', encoding='utf-8').write(text)
    return left


def main(path, replacing=False):
    data = json.load(open(path, encoding='utf-8'))
    home = {}  # key -> file, from the reference language
    for name in FILES:
        for key in keys_of('en', name):
            home[key] = name
    for lang, strings in data.items():
        if replacing:
            before = len(strings)
            strings = replace(lang, strings, home)
            print(f"{lang}: corrected {before - len(strings)}")
        added = 0
        for name in FILES:
            existing = keys_of(lang, name)
            new = {k: v for k, v in strings.items() if home.get(k) == name and k not in existing}
            if not new:
                continue
            target = os.path.join(ROOT, lang, name)
            text = open(target, encoding='utf-8').read()
            block = ''.join(f'  <data name="{k}" xml:space="preserve">\n    <value>{escape(v)}</value>\n  </data>\n'
                            for k, v in new.items())
            text = text.replace('</root>', block + '</root>')
            open(target, 'w', encoding='utf-8').write(text)
            added += len(new)
        print(f"{lang}: added {added}")


if __name__ == '__main__':
    args = [a for a in sys.argv[1:] if a != '--replace']
    sys.exit(main(args[0], '--replace' in sys.argv[1:]))
