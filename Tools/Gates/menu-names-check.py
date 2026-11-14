#!/usr/bin/env python3
"""The editor's context menu and the menu bar say the same thing for the same command (the 2026-11-11 menu report; the
user, 2026-11-13): an item of the context menu (Controls/EditorControls/EditorContainer.xaml) whose command the menu bar
also has (MenuBarItems/*.xaml, the same Command and CommandParameter) has its text from the same string and its shortcut
hint from the same setting the menu bar registers (MenuBarItems/*.xaml.cs, Register*Shortcut(Settings.X, Item)); the
context menu's format buttons (ContextMenuItems/FormatItem.xaml) name the shortcut of the menu bar's item with their
command parameter. Runs on Linux: it reads the XAML and the C#.

    python3 Tools/Gates/menu-names-check.py
"""
import glob
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(__file__), '..', '..', 'Dev', 'Typedown.Core', 'Controls', 'EditorControls')
# The context menu's "Copy as" submenu says "Plain text", "Markdown", "HTML": the menu bar's flat items say "Copy as
# plain text"... - the submenu's name says the rest.
SHORTER_IN_SUBMENU = {'CopyAsPlainTextItem', 'CopyAsMarkdownItem', 'CopyAsHTMLCodeItem'}
# Items known to differ, with why: none now (the context menu's Delete, whose hint the menu bar did not have, went with
# the menu report's item 7, 2026-11-13).
KNOWN = set()

ITEM = re.compile(r'<(?:Toggle)?MenuFlyout(?:Sub)?Item\b[^>]*?>|<controls:StateToggleButton\b[^>]*?>|<Button\b[^>]*?>', re.S)


def attr(tag, pattern):
    m = re.search(pattern, tag)
    return m.group(1) if m else None


def items(path):
    text = open(path, encoding='utf-8-sig').read()
    found = []
    for m in ITEM.finditer(text):
        tag = m.group(0)
        command = attr(tag, r'Command="\{(?:x:Bind|Binding) ([A-Za-z.]+)')
        found.append({
            'name': attr(tag, r'x:Name="(\w+)"'),
            'key': attr(tag, r'LocaleString Key=([A-Za-z.]+)') or attr(tag, r'ToolTip\.TextResource="([A-Za-z.]+)"'),
            'command': command.split('.')[-1] if command else None,
            'parameter': attr(tag, r'CommandParameter="([^"]*)"'),
            'shortcut': attr(tag, r'KeyboardAcceleratorTextOverride="\{x:Bind Settings\.(\w+)') or attr(tag, r'ToolTip\.Shortcut="(\w+)"'),
        })
    return found


def main():
    bar = {}
    for xaml in glob.glob(os.path.join(ROOT, 'MenuBarItems', '*.xaml')):
        registered = {}
        code = xaml + '.cs'
        if os.path.exists(code):
            for setting, item in re.findall(r'Register\w*Shortcut\(Settings\.(\w+), (\w+)\)', open(code, encoding='utf-8-sig').read()):
                registered[item] = setting
        for item in items(xaml):
            if item['command']:
                item['shortcut'] = registered.get(item['name'])
                bar.setdefault((item['command'], item['parameter']), item)
    context = items(os.path.join(ROOT, 'EditorContainer.xaml')) + items(os.path.join(ROOT, 'ContextMenuItems', 'FormatItem.xaml'))
    problems, compared = [], 0
    for item in context:
        twin = bar.get((item['command'], item['parameter']))
        if not item['command'] or not twin:
            continue
        compared += 1
        label = item['name'] or f"{item['command']}({item['parameter']})"
        if item['key'] != twin['key'] and item['name'] not in SHORTER_IN_SUBMENU:
            problems.append(f"{label}: says {item['key']}, the menu bar {twin['key']}")
        if item['shortcut'] != twin['shortcut'] and (item['name'], 'shortcut') not in KNOWN:
            problems.append(f"{label}: shortcut hint {item['shortcut']}, the menu bar's {twin['shortcut']}")
    if compared < 10:
        problems.append(f'only {compared} context menu items matched the menu bar: the check reads the files wrongly')
    for p in problems:
        print('  ' + p)
    print(f'{compared} context menu items compared with the menu bar; {len(problems)} differ')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
