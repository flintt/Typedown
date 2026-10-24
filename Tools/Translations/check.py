#!/usr/bin/env python3
"""Reports how complete each translation is, against en.

    python3 Tools/Translations/check.py            # summary, one line per language
    python3 Tools/Translations/check.py de ja      # the missing keys of those languages
    python3 Tools/Translations/check.py --missing  # every language's missing keys
    python3 Tools/Translations/check.py --strict   # exit 1 if any language has a gap or a known mistranslation (CI)

Strings live in Dev/Typedown.Core/Resources/Strings/<lang>/<file>.resw. A key that a
language does not have falls back to the project's default language at runtime, so a gap
shows up as a mixed-language interface rather than an error.
"""
import html, json, os, re, sys

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'Dev', 'Typedown.Core', 'Resources', 'Strings')
FILES = ['CommonResources.resw', 'DialogResources.resw', 'SettingsResources.resw', 'Resources.resw']

def strip_comments(text):
    """The .resw template carries sample entries (Name1, Bitmap1, Icon1) inside a comment."""
    return re.sub(r'<!--.*?-->', '', text, flags=re.S)



def entries(lang, name):
    path = os.path.join(ROOT, lang, name)
    if not os.path.exists(path):
        return {}
    text = strip_comments(open(path, encoding='utf-8').read())
    return {m.group(1): m.group(2) for m in re.finditer(r'<data name="([^"]+)"[^>]*>\s*<value>(.*?)</value>', text, re.S)}


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('-')]
    show_missing = '--missing' in sys.argv or bool(args)
    reference = {f: entries('en', f) for f in FILES}
    total = sum(len(v) for v in reference.values())
    langs = sorted(d for d in os.listdir(ROOT) if os.path.isdir(os.path.join(ROOT, d)))
    if args:
        langs = [l for l in langs if l in args]
    rows = []
    for lang in langs:
        gaps = {f: [k for k in reference[f] if k not in entries(lang, f)] for f in FILES}
        have = total - sum(len(v) for v in gaps.values())
        rows.append((have / total, lang, have, gaps))
    for pct, lang, have, gaps in sorted(rows, reverse=True):
        print(f"{lang:10} {have:4}/{total}  {pct*100:5.1f}%")
        if show_missing:
            for f, keys in gaps.items():
                for key in keys:
                    print(f"    {f.replace('Resources.resw',''):9} {key} = {reference[f][key]}")
    incomplete = [r for r in rows if r[0] < 1]
    print(f"\n{len(rows) - len(incomplete)}/{len(rows)} complete; {len(incomplete)} with gaps")
    wrong = mistranslations(langs)
    for line in wrong:
        print("WRONG " + line)
    print(f"{len(wrong)} known mistranslation(s)")
    if '--strict' in sys.argv and (incomplete or wrong):
        # A gap is not an error at runtime — the key falls back to English — so nothing fails until someone
        # sees one English line in their own language. This is what makes it fail earlier.
        if incomplete:
            print("run with --missing to see them, and add them to every language before merging")
        if wrong:
            print("put them right with Tools/Translations/apply.py --replace")
        return 1
    return 0


def mistranslations(langs):
    """A word translated by its dictionary sense, back again: the values Tools/Translations/mistranslations.json
    lists for a key, and a switch's Off that says Close (most languages had it, from an English "Close")."""
    path = os.path.join(os.path.dirname(__file__), 'mistranslations.json')
    known = json.load(open(path, encoding='utf-8')) if os.path.exists(path) else {}
    found = []
    for lang in langs:
        values = {}
        for f in FILES:
            values.update({k: html.unescape(v).strip() for k, v in entries(lang, f).items()})
        for key, bad in known.get(lang, {}).items():
            if values.get(key) in [b.strip() for b in bad]:
                found.append(f"{lang} {key} = {values[key]} (a known mistranslation)")
        # Thai, Lao, Khmer and Bengali say off and close with one word (ปิด, ປິດ, បិទ, বন্ধ), as Windows' own switches do.
        if lang not in ('th', 'lo', 'km', 'bn') and values.get('Off') and values.get('Off') == values.get('Close'):
            found.append(f"{lang} Off = {values['Off']}, the same as Close: a switch's off, not closing")
    return found


if __name__ == '__main__':
    sys.exit(main())
