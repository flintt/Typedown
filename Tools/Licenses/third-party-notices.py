#!/usr/bin/env python3
"""Writes ThirdPartyNotices.txt: the software of others that this program carries, and the terms it is used under.

    python3 Tools/Licenses/third-party-notices.py            write ThirdPartyNotices.txt at the repository root
    python3 Tools/Licenses/third-party-notices.py --check    exit 1 if the file there is not what would be written

Only what ships is listed, not the tools that build it. The editor page (Dev/Typedown.Editor, bundled by webpack into
Dev/Typedown/Resources/Statics) brings the packages named under "dependencies" in its package.json and, through them,
the dependencies each of those names in its own package.json, found the way Node finds them (the package's own
node_modules first, then the folders above it, then the top one). The build's own packages among those dependencies
(BUILD_ONLY: react-scripts, sass, imports-loader) are not followed, and a package reached only through them is not
listed. A few packages the bundle holds without the "dependencies" naming them are added (BUNDLED: a devDependency
the source imports, the shim webpack is told to provide, the helpers Babel puts in). For each package its
package.json gives the name, version, licence and home; its LICENSE / LICENCE / COPYING file (any case, any
extension) the copyright lines and the terms, and a NOTICE file is copied as it is.

Then what is not a package: the code copied into the editor's source (Muya from MarkText with its marked parser, the
sequence diagrams and Snap.svg in src/assets/libs, Pele in public/) and the fonts the page ships
(Statics/static/media), each with the licence file next to it where the folder has one (VENDORED below says where
each comes from). And the host's NuGet packages: the PackageReference items of the projects that ship (PROJECTS: the
app, its library, the automation library, the command line and its MCP server; not the tests), with what their
.nuspec files ask for in turn (the dependency group nearest net10.0), read from the NuGet cache (NUGET_PACKAGES, else
~/.nuget/packages). A reference with ExcludeAssets="all" ships nothing and is left out, so are the build tools
(Fody, the Windows SDK build tools, analyzers) and the System.* packages .NET itself already has. Microsoft's own
packages are listed by name, version and licence (the expression, or the licence terms their package points to); a
proprietary Microsoft licence is named, not copied.

A licence file that is the standard text (once its copyright lines and title are taken away) is not repeated in the
entry: each standard licence is written once at the end and the entry points to it. A licence file in other words is
copied into the entry as it is. The output is sorted by name and the same for the same inputs (UTF-8, LF). What
needs a person's eye (no licence found, a licence this script does not know, a copyleft one) is printed on stderr.

Exit codes: 0 written / up to date, 1 --check found the file out of date, 2 an input missing (no node_modules).
"""
import argparse
import json
import os
import re
import sys
import textwrap
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
EDITOR = os.path.join(ROOT, "Dev", "Typedown.Editor")
NODE_MODULES = os.path.join(EDITOR, "node_modules")
OUTPUT = os.path.join(ROOT, "ThirdPartyNotices.txt")
NUGET = os.environ.get("NUGET_PACKAGES") or os.path.join(os.path.expanduser("~"), ".nuget", "packages")

# The editor's build tools, named among its "dependencies" but not in the bundle.
BUILD_ONLY = {"react-scripts", "sass", "imports-loader"}
# In the bundle, though no "dependencies" list names them (main.*.js.LICENSE.txt shows them there).
BUNDLED = {
    "popper.js": "a devDependency, imported by Muya's floating boxes (src/components/Muya/lib/ui/baseFloat)",
    "process": "the browser shim webpack provides as process (config-overrides.js)",
    "@babel/runtime": "the helpers and regenerator runtime Babel puts into the bundle",
}
NPM_NOTES = {
    "katex": "Includes the KaTeX fonts (KaTeX_*.ttf / .woff / .woff2 in static/media), under the same licence.",
    "@marktext/file-icons": "Carries the icon fonts of file-icons (listed under their own names here).",
}
# A check that the walk found the main ones (a missing one is reported on stderr).
EXPECTED = ["codemirror", "katex", "mermaid", "d3", "dagre-d3-es", "cytoscape", "vega", "vega-lite", "vega-embed",
            "prismjs", "dompurify", "turndown", "snabbdom", "react", "react-dom", "flowchart.js", "snapsvg-cjs",
            "underscore", "webfontloader", "@marktext/file-icons", "github-markdown-css", "element-resize-detector"]
# The projects whose packages ship with the program.
PROJECTS = [
    "Dev/Typedown/Typedown.csproj",
    "Dev/Typedown.Core/Typedown.Core.csproj",
    "Dev/Typedown.Automation/Typedown.Automation.csproj",
    "Tools/Typedown.Cli/Typedown.Cli.csproj",
    "Tools/Typedown.Mcp/Typedown.Mcp.csproj",
]
# NuGet packages that only build: nothing of theirs is in the output.
NUGET_BUILD_ONLY = {"fody", "microsoft.windows.sdk.buildtools", "microsoft.windows.sdk.buildtools.msix",
                    "microsoft.entityframeworkcore.analyzers", "microsoft.windows.cswin32"}
# Packages .NET (net10.0) has in itself: their files lose to the runtime's own and are not shipped.
NUGET_INBOX = re.compile(r"^(system\.|microsoft\.csharp$|netstandard\.library$|microsoft\.netcore\.|runtime\.|"
                         r"microsoft\.win32\.(registry|primitives)$|microsoft\.bcl\.(asyncinterfaces|hashcode)$)")
# ... except these, which are packages only (not in the shared framework).
NUGET_NOT_INBOX = {"system.reactive", "system.drawing.common"}
NUGET_NOTES = {
    "sqlitepclraw.lib.e_sqlite3": "Contains the SQLite library (e_sqlite3), which is in the public domain "
                                  "(https://www.sqlite.org/copyright.html).",
    "propertychanged.fody": "The weaver runs at build time; its attribute assembly PropertyChanged.dll is copied to "
                            "the output.",
    "utf.unknown": "A port of Mozilla's universal charset detector, used unmodified. Licensed under the Mozilla "
                   "Public License 1.1 (https://www.mozilla.org/MPL/1.1/); its source code is available at "
                   "https://github.com/CharsetDetector/UTF-unknown.",
}

STATICS = os.path.join(ROOT, "Dev", "Typedown", "Resources", "Statics")
MUYA = "Dev/Typedown.Editor/src/components/Muya"
FONTS = MUYA + "/themes/fonts"

MARKTEXT_MIT = ("Copyright (c) 2017-present Luo Ran\n"
                "Copyright (c) 2018-present MarkText Contributors")

# What is not an npm package: name, version, licence, home, copyright lines, licence file (repository path, copied when
# it is not a standard text), what it is in this program, and files whose presence says it ships.
VENDORED = [
    dict(name="Muya (MarkText editor core)", version="(copy)", licence="MIT",
         url="https://github.com/marktext/marktext", copyright=MARKTEXT_MIT,
         used="The editing core, copied into " + MUYA + " and changed there.", needs=[MUYA]),
    dict(name="marked (patched copy in Muya)", version="0.8.2 with fixes from 1.2.5", licence="MIT",
         url="https://github.com/markedjs/marked", file=MUYA + "/lib/parser/marked/LICENSE",
         used="Muya's Markdown parser (" + MUYA + "/lib/parser/marked).", needs=[MUYA + "/lib/parser/marked"]),
    dict(name="Django urlify.js", version="(copy)", licence="BSD-3-Clause",
         url="https://github.com/django/django", copyright="Copyright (c) Django Software Foundation and "
         "individual contributors.\nAll rights reserved.",
         used="The transliteration table in " + MUYA + "/lib/parser/marked/urlify.js.",
         needs=[MUYA + "/lib/parser/marked/urlify.js"]),
    dict(name="js-sequence-diagrams", version="2.0.1", licence="BSD-2-Clause",
         url="https://github.com/bramp/js-sequence-diagrams", copyright="Copyright (c) 2012-2017, Andrew Brampton\n"
         "All rights reserved.", used="Sequence diagrams (src/assets/libs/sequence-diagram-snap.js).",
         needs=["Dev/Typedown.Editor/src/assets/libs/sequence-diagram-snap.js"]),
    dict(name="Snap.svg", version="0.5.1", licence="Apache-2.0", url="https://github.com/adobe-webplatform/Snap.svg",
         copyright="Copyright (c) 2013-2017 Adobe Systems Incorporated. All rights reserved.",
         used="SVG drawing for the sequence diagrams (src/assets/libs/snap.svg-min.js).",
         needs=["Dev/Typedown.Editor/src/assets/libs/snap.svg-min.js"]),
    dict(name="Pele", version=None, version_from=("Dev/Typedown.Editor/public/pele.global.js", r"Pele (\d[\w.]*)"),
         licence="MIT AND ISC", url="https://github.com/obsidianmd/pele",
         file="Dev/Typedown.Editor/public/pele.LICENSE.txt",
         used="The diagram and picture viewer (public/pele.global.js), with an icon from Lucide.",
         needs=["Dev/Typedown.Editor/public/pele.global.js"]),
    dict(name="Font: DejaVu Sans Mono", version="(fonts)", licence="Bitstream-Vera",
         url="https://dejavu-fonts.github.io/", file=FONTS + "/DejaVuSansMono_LICENSE", standard="Bitstream-Vera",
         copyright="Copyright (c) 2003 by Bitstream, Inc. All Rights Reserved. Bitstream Vera is a trademark of "
         "Bitstream, Inc.\nCopyright (c) 2006 by Tavmjong Bah. All Rights Reserved.\nDejaVu changes are in the public "
         "domain.", used="DejaVuSansMono*.ttf in static/media.", needs=[FONTS + "/DejaVuSansMono.ttf"]),
    dict(name="Font: Open Sans", version="v27 (latin-ext, latin)", licence="Apache-2.0",
         url="https://fonts.google.com/specimen/Open+Sans", file=FONTS + "/open-sans-LICENSE.txt",
         copyright="Copyright 2010-2011 Google Corporation (Digitized data copyright Steve Matteson)",
         used="open-sans-v27-*.woff in static/media; the licence is the one shipped with these files.",
         needs=[FONTS + "/open-sans-v27-latin-ext_latin-regular.woff"]),
    dict(name="Font: Daniel (danielbd)", version="(font)", licence="LicenseRef-Daniel-Font",
         url="https://www.dafont.com/daniel.font", copyright="Daniel font by Daniel Midgley",
         text="The Daniel font is free to use for any purpose (as stated by its author and by js-sequence-diagrams,"
              "\nwhich ships it for its hand-drawn theme).",
         used="danielbd.woff / danielbd.woff2 in static/media, for the hand-drawn sequence diagrams.",
         needs=["Dev/Typedown.Editor/src/assets/styles/danielbd.woff2"]),
    dict(name="Font: Font Awesome", version="4.7.0 (from file-icons)", licence="OFL-1.1",
         url="https://fontawesome.com/v4/", copyright="Font Awesome by Dave Gandy - https://fontawesome.com",
         used="fontawesome.woff2 in static/media (file-type icons, from @marktext/file-icons).",
         needs=["Dev/Typedown.Editor/node_modules/@marktext/file-icons/build/fonts/fontawesome.woff2"]),
    dict(name="Font: Octicons", version="(from file-icons)", licence="MIT", url="https://github.com/primer/octicons",
         copyright="Copyright (c) GitHub Inc.", used="octicons.woff2 in static/media (from @marktext/file-icons).",
         needs=["Dev/Typedown.Editor/node_modules/@marktext/file-icons/build/fonts/octicons.woff2"]),
    dict(name="Font: MFixx (Font Mfizz)", version="(from file-icons)", licence="MIT",
         url="https://github.com/file-icons/MFixx", copyright="Copyright (c) 2013 Mfizz Inc., Joe Lauer",
         used="mfixx.woff2 in static/media (from @marktext/file-icons).",
         needs=["Dev/Typedown.Editor/node_modules/@marktext/file-icons/build/fonts/mfixx.woff2"]),
    dict(name="Font: DevOpicons (Devicons)", version="(from file-icons)", licence="MIT",
         url="https://github.com/file-icons/DevOpicons", copyright="Copyright (c) 2014 Theodore Vorillas",
         used="devopicons.woff2 in static/media (from @marktext/file-icons).",
         needs=["Dev/Typedown.Editor/node_modules/@marktext/file-icons/build/fonts/devopicons.woff2"]),
    dict(name="Microsoft .NET runtime", version="10.0", licence="MIT", url="https://github.com/dotnet/runtime",
         copyright="Copyright (c) .NET Foundation and Contributors",
         used="The program is published self-contained: the .NET runtime ships with it. Its own third-party notices "
              "are at https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT.", needs=[]),
    dict(name="Microsoft Edge WebView2 Runtime bootstrapper", version="(MicrosoftEdgeWebview2Setup.exe)",
         licence="Microsoft Software License Terms", url="https://developer.microsoft.com/microsoft-edge/webview2/",
         copyright="Copyright (c) Microsoft Corporation. All rights reserved.",
         used="Installs the WebView2 Runtime where Windows does not have it. Microsoft's terms: "
              "https://developer.microsoft.com/microsoft-edge/webview2/#download",
         needs=["Dev/Typedown/MicrosoftEdgeWebview2Setup.exe"], microsoft=True),
]

INTRO = """THIRD-PARTY SOFTWARE NOTICES

This program includes the following third-party software. Each is the property of its authors and is used under the
licence shown.

Each entry gives the component's name, version, licence, home and copyright lines. Where a component's licence is a
standard text, that text is printed once in the last part of this file ("Licence texts"); where its licence file says
something of its own, the file is printed in the entry as it is. NOTICE files are printed in their entries in full.
"""

MIT = """Copyright (c) <year> <copyright holders>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"""

ISC = """Copyright (c) <year> <copyright holders>

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
"""

ZERO_BSD = """Copyright (c) <year> <copyright holders>

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
"""

BSD_CLAUSES = """Copyright (c) <year> <copyright holders>

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
{third}
THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
"""
BSD2 = BSD_CLAUSES.format(third="")
BSD3 = BSD_CLAUSES.format(third="""
3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.
""")

OFL = """Copyright (c) <year> <copyright holders>

This Font Software is licensed under the SIL Open Font License, Version 1.1.

SIL OPEN FONT LICENSE

Version 1.1 - 26 February 2007

PREAMBLE

The goals of the Open Font License (OFL) are to stimulate worldwide development of collaborative font projects, to support the font creation efforts of academic and linguistic communities, and to provide a free and open framework in which fonts may be shared and improved in partnership with others.

The OFL allows the licensed fonts to be used, studied, modified and redistributed freely as long as they are not sold by themselves. The fonts, including any derivative works, can be bundled, embedded, redistributed and/or sold with any software provided that any reserved names are not used by derivative works. The fonts and derivatives, however, cannot be released under any other type of license. The requirement for fonts to remain under this license does not apply to any document created using the fonts or their derivatives.

DEFINITIONS

"Font Software" refers to the set of files released by the Copyright Holder(s) under this license and clearly marked as such. This may include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the copyright statement(s).

"Original Version" refers to the collection of Font Software components as distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting, or substituting — in part or in whole — any of the components of the Original Version, by changing formats or by porting the Font Software to a new environment.

"Author" refers to any designer, engineer, programmer, technical writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS

Permission is hereby granted, free of charge, to any person obtaining a copy of the Font Software, to use, study, copy, merge, embed, modify, redistribute, and sell modified and unmodified copies of the Font Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components, in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled, redistributed and/or sold with any software, provided that each copy contains the above copyright notice and this license. These can be included either as stand-alone text files, human-readable headers or in the appropriate machine-readable metadata fields within text or binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font Name(s) unless explicit written permission is granted by the corresponding Copyright Holder. This restriction only applies to the primary font name as presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font Software shall not be used to promote, endorse or advertise any Modified Version, except to acknowledge the contribution(s) of the Copyright Holder(s) and the Author(s) or with their explicit written permission.

5) The Font Software, modified or unmodified, in part or in whole, must be distributed entirely under this license, and must not be distributed under any other license. The requirement for fonts to remain under this license does not apply to any document created using the Font Software.

TERMINATION

This license becomes null and void if any of the above conditions are not met.

DISCLAIMER

THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM OTHER DEALINGS IN THE FONT SOFTWARE.
"""


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read().replace("\r\n", "\n").replace("\r", "\n").replace("﻿", "")


def rel(path):
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def wrap_paragraphs(text, width=80):
    out = []
    for para in text.split("\n"):
        out.append(textwrap.fill(para, width, break_long_words=False, break_on_hyphens=False) if len(para) > width
                   else para)
    return "\n".join(out)


def standard_texts():
    # Apache-2.0 and the Bitstream Vera / DejaVu licence come from files in the repository (the Open Sans and DejaVu
    # licence files the fonts ship with: the Apache one is the Apache Software Foundation's text word for word).
    return {
        "MIT": MIT,
        "ISC": ISC,
        "0BSD": ZERO_BSD,
        "BSD-2-Clause": BSD2,
        "BSD-3-Clause": BSD3,
        "Apache-2.0": read(os.path.join(ROOT, FONTS, "open-sans-LICENSE.txt")).strip("\n") + "\n",
        "OFL-1.1": wrap_paragraphs(OFL),
        "Bitstream-Vera": read(os.path.join(ROOT, FONTS, "DejaVuSansMono_LICENSE")).strip("\n") + "\n",
    }


LICENCE_TITLES = {
    "MIT": "MIT License", "ISC": "ISC License", "0BSD": "BSD Zero Clause License",
    "BSD-2-Clause": "BSD 2-Clause License", "BSD-3-Clause": "BSD 3-Clause License",
    "Apache-2.0": "Apache License 2.0", "OFL-1.1": "SIL Open Font License 1.1",
    "Bitstream-Vera": "Bitstream Vera Fonts License (DejaVu fonts licence)",
}

# -- comparing a licence file with the standard texts ---------------------------------------------------------------

COPYRIGHT_LINE = re.compile(r"^[\s*#/>-]*(copyright\b|\(c\)\s*\d|©|portions copyright)", re.I)
NOT_A_HOLDER = re.compile(r"copyright (notice|holder|owner|law|license|statement|and|\[yyyy\])|"
                          r"\[yyyy\]|<year>|\{yyyy\}", re.I)


# a line of the licence's own words that happens to start with "copyright" (not a holder's line)
LICENCE_WORDS = re.compile(r"copyright (notice|holders?|owners?|law|license|statement)", re.I)


def holder_line(line):
    """A line that says who holds a copyright (not the licence's own words about "copyright holders")."""
    s = line.strip().strip("*#/").strip()
    return bool(COPYRIGHT_LINE.match(line)) and not NOT_A_HOLDER.search(s) and len(s) < 300


def copyright_lines(text):
    lines = []
    for line in text.split("\n"):
        s = line.strip().strip("*#/").strip()
        if holder_line(line) and s not in lines:
            lines.append(s)
    return lines


def words(text):
    text = text.lower().replace("’", "'").replace("&", " and ")
    return re.findall(r"[a-z0-9]+", text)


def body_words(text):
    keep = []
    for line in text.split("\n"):
        s = line.strip().strip("*#/").strip()
        if (COPYRIGHT_LINE.match(line) and not LICENCE_WORDS.match(s)) or re.fullmatch(r"all rights reserved\.?", s, re.I):
            continue
        keep.append(line)
    w = " ".join(words("\n".join(keep)))
    w = re.sub(r"\ball rights reserved\b", " ", w)
    w = re.sub(r"\b\d{1,2}\b", " ", w)  # list and section numbers
    # the parts of the permissive texts that differ only in who is named
    w = re.sub(r"neither the name of .{0,200}? nor the names of (its |their |the |any )?contributors",
               "neither the name of X nor the names of its contributors", w)
    w = re.sub(r"\b(the )?(copyright holders?|copyright owners?|authors? or copyright holders?|regents|"
               r"author\(?s?\)?|authors?|isc|contributors|[a-z]+ contributors)( and contributors)?\b"
               r"(?= (disclaims|be liable|or contributors be liable|as is))",
               "HOLDER", w)
    w = re.sub(r"provided by (the )?(HOLDER|copyright holders and contributors)( and contributors)?", "provided by HOLDER", w)
    w = re.sub(r"shall (the )?HOLDER( or contributors)?", "shall HOLDER", w)
    return " ".join(w.split())


def is_standard(text, template):
    """True when text is the template's licence, give or take the copyright lines, a short title and who is named."""
    t = body_words(template)
    b = body_words(text)
    head = t.split()[:6]
    i = b.find(" ".join(head))
    if i < 0:
        return False
    prefix, rest = b[:i].split(), b[i:]
    if len(prefix) > 10:
        return False
    if rest == t:
        return True
    # Apache: the appendix ("How to apply...") may be there, filled in or not, or left out.
    end = "end of terms and conditions"
    if end in t and rest.startswith(t[:t.index(end) + len(end)]):
        tail = rest[t.index(end) + len(end):].split()
        appendix = t[t.index(end) + len(end):].split()
        return not tail or (" ".join(tail).startswith("appendix how to apply the apache license")
                            and len(tail) <= len(appendix) + 30)
    return False


def classify(text, ids, standards):
    """The standard licence text is, if any (tried in the order of the licence expression, then all)."""
    order = [i for i in ids if i in standards] + [i for i in sorted(standards) if i not in ids]
    for lid in order:
        if is_standard(text, standards[lid]):
            return lid
    return None


def licence_ids(expr):
    return [t for t in re.findall(r"[A-Za-z0-9.+\-]+", expr or "") if t.upper() not in ("AND", "OR", "WITH")]


COPYLEFT = re.compile(r"\b(A?GPL|LGPL|MPL|EPL|CDDL|EUPL|OSL|CPL|CC-BY-SA|CC-BY-NC|SSPL)", re.I)
PERMISSIVE = {"MIT", "ISC", "0BSD", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "OFL-1.1", "Bitstream-Vera",
              "Unlicense", "CC0-1.0", "Zlib", "BlueOak-1.0.0", "Python-2.0"}

# -- npm -------------------------------------------------------------------------------------------------------------


def resolve(name, from_dir):
    d = from_dir
    while True:
        cand = os.path.join(d, "node_modules", name)
        if os.path.isfile(os.path.join(cand, "package.json")):
            return cand
        if os.path.normpath(d) == os.path.normpath(EDITOR):
            return None
        parent = os.path.dirname(d)
        # step out of a node_modules folder (and a scope folder) to the package above
        if os.path.basename(parent) == "node_modules":
            parent = os.path.dirname(parent)
        elif os.path.basename(os.path.dirname(parent)) == "node_modules" and os.path.basename(parent).startswith("@"):
            parent = os.path.dirname(os.path.dirname(parent))
        if parent == d:
            return None
        d = parent


def npm_closure(problems):
    pkg = json.loads(read(os.path.join(EDITOR, "package.json")))
    roots = sorted(n for n in pkg.get("dependencies", {}) if n not in BUILD_ONLY) + sorted(BUNDLED)
    seen = {}
    stack = [(n, EDITOR, n) for n in reversed(roots)]
    while stack:
        name, from_dir, via = stack.pop()
        path = resolve(name, from_dir)
        if path is None:
            problems.append(f"npm: {name} (needed by {via}) is not in node_modules")
            continue
        real = os.path.realpath(path)
        if real in seen:
            continue
        meta = json.loads(read(os.path.join(path, "package.json")))
        seen[real] = (path, meta)
        for dep in sorted(meta.get("dependencies", {}), reverse=True):
            stack.append((dep, path, meta.get("name", name)))
    return [v for _, v in sorted(seen.items())]


def repo_url(meta):
    r = meta.get("repository")
    if isinstance(r, dict):
        r = r.get("url")
    if isinstance(r, str) and r:
        r = r.strip()
        r = re.sub(r"^git\+", "", r)
        r = re.sub(r"^git://", "https://", r)
        r = re.sub(r"^ssh://git@", "https://", r)
        r = re.sub(r"^git@github\.com:", "https://github.com/", r)
        r = re.sub(r"^github:", "https://github.com/", r)
        if re.fullmatch(r"[\w.-]+/[\w.-]+", r):
            r = "https://github.com/" + r
        r = re.sub(r"\.git$", "", r)
        r = r.replace("http://github.com", "https://github.com")
        return r
    h = meta.get("homepage")
    return h if isinstance(h, str) and h else None


def npm_licence(meta):
    lic = meta.get("license")
    if isinstance(lic, dict):
        lic = lic.get("type")
    if not lic and isinstance(meta.get("licenses"), list):
        types = [x.get("type") if isinstance(x, dict) else str(x) for x in meta["licenses"]]
        lic = " OR ".join(t for t in types if t)
        if len(types) > 1:
            lic = "(" + lic + ")"
    return lic or None


LICENCE_FILE = re.compile(r"^(licen[cs]e|copying)([.\-_].*)?$", re.I)
NOTICE_FILE = re.compile(r"^notice([.\-_].*)?$", re.I)
CODE_FILE = re.compile(r"\.(m?js|cjs|ts|json|sh|py|ps1)$", re.I)  # license-update.mjs is a script, not a licence


def files_like(path, pattern):
    try:
        names = sorted(os.listdir(path))
    except OSError:
        return []
    return [os.path.join(path, n) for n in names if pattern.match(n) and not CODE_FILE.search(n)
            and os.path.isfile(os.path.join(path, n))]


def author_line(meta):
    a = meta.get("author")
    if isinstance(a, dict):
        a = a.get("name")
    return a if isinstance(a, str) and a.strip() else None


def npm_entries(standards, problems):
    entries, done = [], set()
    for path, meta in npm_closure(problems):
        name, version = meta.get("name") or os.path.basename(path), meta.get("version", "?")
        if (name, version) in done:  # the same package installed twice (under two packages' node_modules)
            continue
        done.add((name, version))
        lic = npm_licence(meta)
        texts = [(os.path.basename(f), read(f)) for f in files_like(path, LICENCE_FILE)]
        text = "\n\n".join(t for _, t in texts)
        notice = "\n\n".join(read(f).strip("\n") for f in files_like(path, NOTICE_FILE))
        e = dict(name=name, version=version, licence=lic or "(not stated)", url=repo_url(meta), kind="npm")
        if name in BUNDLED:
            e["used"] = "In the bundle as " + BUNDLED[name] + "."
        if name in NPM_NOTES:
            e["used"] = NPM_NOTES[name]
        e["notice"] = notice or None
        e["copyright"] = copyright_lines(text) if text else []
        if not e["copyright"] and author_line(meta):
            e["copyright"] = ["Author: " + author_line(meta)]
        settle_text(e, texts, standards, problems, "npm " + name + "@" + version)
        entries.append(e)
    return entries


def settle_text(e, texts, standards, problems, label):
    """Sets the entry's standard licences (e["standard"], a list) and its own licence text (e["text"]) from its licence
    files [(file name, text)]: a file that is a standard text points to it, any other is copied."""
    ids = licence_ids(e["licence"])
    texts = [(n, t.strip("\n")) for n, t in texts if t.strip()]
    if texts:
        std, own = [], []
        for n, t in texts:
            lid = classify(t, ids, standards)
            if lid and lid not in std:
                std.append(lid)
            elif not lid:
                own.append((n, t))
        if std and own and re.search(r"\bOR\b", e["licence"]):
            # one of several licences to choose from: the program takes the one whose standard text is there
            e["used"] = ((e.get("used") or "") + f" Offered under {e['licence']}; used here under {std[0]}.").strip()
            own = []
        if std:
            e["standard"] = std
            if not ids or e["licence"] == "(not stated)":
                e["licence"] = " AND ".join(std)
                problems.append(f"{label}: no licence in its metadata; its licence file is the {std[0]} text")
        if own:
            e["text"] = own[0][1] if len(own) == 1 else "\n\n".join(f"[{n}]\n\n{t}" for n, t in own)
    else:
        known = [i for i in ids if i in standards]
        if known:
            e["standard"] = known
            problems.append(f"{label}: no licence file; licence from its metadata ({e['licence']})")
        else:
            problems.append(f"{label}: no licence file and no known licence text ({e['licence']})")


# -- vendored files and fonts ----------------------------------------------------------------------------------------


def vendored_entries(standards, problems):
    entries = []
    for v in VENDORED:
        if any(not os.path.exists(os.path.join(ROOT, n)) for n in v["needs"]):
            continue
        version = v["version"]
        if v.get("version_from"):
            path, pattern = v["version_from"]
            m = re.search(pattern, read(os.path.join(ROOT, path))[:2000])
            version = m.group(1) if m else "?"
        e = dict(name=v["name"], version=version, licence=v["licence"], url=v["url"], kind="vendored",
                 used=v.get("used"), notice=None, microsoft=v.get("microsoft", False))
        text = read(os.path.join(ROOT, v["file"])) if v.get("file") else ""
        e["copyright"] = v["copyright"].split("\n") if v.get("copyright") else copyright_lines(text)
        if v.get("standard"):
            e["standard"] = [v["standard"]]
        elif v.get("text"):
            e["text"] = v["text"]
        elif e["microsoft"]:
            pass
        elif text:
            settle_text(e, [(v["file"], text)], standards, problems, v["name"])
        else:
            ids = [i for i in licence_ids(v["licence"]) if i in standards]
            if ids:
                e["standard"] = ids
            else:
                problems.append(f"{v['name']}: no licence text")
        entries.append(e)
    return entries

# -- NuGet -----------------------------------------------------------------------------------------------------------


def version_key(v):
    parts = re.split(r"[.\-+]", v)
    return [(0, int(p), "") if p.isdigit() else (1, 0, p) for p in parts]


def min_version(spec):
    spec = spec.strip()
    m = re.match(r"^[\[(]\s*([^,\])]*)", spec)
    return (m.group(1).strip() if m else spec) or "0"


def tfm_rank(tfm):
    """How near a dependency group's framework is to net10.0-windows (lower is nearer); None when it does not apply."""
    t = (tfm or "").lower().replace(".netcoreapp", "netcoreapp").replace(".netstandard", "netstandard")
    if t == "":
        return 1000
    m = re.fullmatch(r"net(\d+)\.(\d+)(-windows[\d.]*)?", t)
    if m and int(m.group(1)) >= 5:
        major = int(m.group(1))
        return None if major > 10 else (10 - major) * 2 + (0 if m.group(3) else 1)
    m = re.fullmatch(r"netcoreapp(\d+)\.(\d+)", t)
    if m:
        return 100 - int(m.group(1)) * 10 - int(m.group(2))
    m = re.fullmatch(r"netstandard(\d+)\.(\d+)", t)
    if m:
        return 200 - int(m.group(1)) * 10 - int(m.group(2))
    return None


def nuspec(pid, version):
    d = os.path.join(NUGET, pid.lower(), version.lower())
    p = os.path.join(d, pid.lower() + ".nuspec")
    if not os.path.isfile(p):
        cands = [f for f in os.listdir(d) if f.endswith(".nuspec")] if os.path.isdir(d) else []
        if not cands:
            return None, d
        p = os.path.join(d, cands[0])
    root = ET.fromstring(read(p).encode("utf-8"))
    for el in root.iter():
        if "}" in el.tag:
            el.tag = el.tag.split("}", 1)[1]
    return root.find("metadata"), d


def installed_version(pid, wanted):
    d = os.path.join(NUGET, pid.lower())
    if not os.path.isdir(d):
        return None
    have = sorted(os.listdir(d), key=version_key)
    if wanted.lower() in have:
        return wanted.lower()
    up = [v for v in have if version_key(v) >= version_key(wanted)]
    return up[0] if up else None


def project_references():
    refs = {}
    for proj in PROJECTS:
        path = os.path.join(ROOT, proj)
        if not os.path.isfile(path):
            continue
        root = ET.fromstring(read(path).encode("utf-8"))
        for el in root.iter("PackageReference"):
            pid, ver = el.get("Include"), el.get("Version")
            if not pid or not ver:
                continue
            excluded = (el.get("ExcludeAssets") or "").lower() == "all"
            cur = refs.get(pid.lower())
            if cur is None or (cur["excluded"] and not excluded) or (
                    cur["excluded"] == excluded and version_key(ver) > version_key(cur["version"])):
                refs[pid.lower()] = {"id": pid, "version": ver, "excluded": excluded}
    return refs


def nuget_closure(problems):
    refs = project_references()
    excluded = {k for k, v in refs.items() if v.get("excluded")}
    wanted = {k: v["version"] for k, v in refs.items() if not v.get("excluded")}
    names = {k: v["id"] for k, v in refs.items()}
    direct = set(wanted)
    changed = True
    while changed:
        changed = False
        for key in sorted(wanted):
            if key in NUGET_BUILD_ONLY:
                continue
            meta, _ = nuspec(names[key], installed_version(names[key], wanted[key]) or wanted[key])
            if meta is None:
                continue
            groups = meta.findall("dependencies/group")
            deps = meta.findall("dependencies/dependency")
            if groups:
                ranked = [(tfm_rank(g.get("targetFramework")), i, g) for i, g in enumerate(groups)]
                ranked = [r for r in ranked if r[0] is not None]
                deps = min(ranked)[2].findall("dependency") if ranked else []
            for dep in deps:
                did, dver = dep.get("id"), min_version(dep.get("version", "0"))
                k = did.lower()
                if k in excluded or k in NUGET_BUILD_ONLY:
                    continue
                if NUGET_INBOX.match(k) and k not in NUGET_NOT_INBOX:
                    continue
                names.setdefault(k, did)
                if k not in wanted or version_key(dver) > version_key(wanted[k]):
                    wanted[k] = dver
                    changed = True
    out = []
    for k in sorted(wanted):
        if k in NUGET_BUILD_ONLY:
            continue
        out.append((names[k], wanted[k], k in direct))
    return out


def is_microsoft(meta, pid):
    authors = (meta.findtext("authors") or "") if meta is not None else ""
    return authors.strip().lower() in ("microsoft", "microsoft corporation") or pid.lower().startswith("microsoft.")


def nuget_entries(standards, problems):
    entries = []
    for pid, want, _direct in nuget_closure(problems):
        ver = installed_version(pid, want)
        meta, folder = nuspec(pid, ver or want)
        e = dict(name=pid, version=ver or want, kind="nuget", notice=None, url=None, copyright=[])
        if meta is None:
            # not in this machine's NuGet cache: what is known of it
            e["licence"] = "MIT" if pid.lower().startswith(("system.", "microsoft.extensions.")) else "(unknown)"
            e["microsoft"] = e["licence"] == "MIT"
            e["copyright"] = ["Copyright (c) .NET Foundation and Contributors"] if e["microsoft"] else []
            e["url"] = "https://www.nuget.org/packages/" + pid + "/" + want
            if e["microsoft"]:
                e["standard"] = ["MIT"]
            problems.append(f"NuGet {pid} {want}: not in the NuGet cache ({NUGET}); licence assumed: {e['licence']}")
            entries.append(e)
            continue
        e["name"] = meta.findtext("id") or pid
        e["version"] = meta.findtext("version") or e["version"]
        e["url"] = (meta.findtext("projectUrl") or "").strip() or None
        cr = (meta.findtext("copyright") or "").strip()
        e["copyright"] = [cr] if cr else []
        e["microsoft"] = is_microsoft(meta, pid)
        lic = meta.find("license")
        lic_url = (meta.findtext("licenseUrl") or "").strip()
        lic_text = ""
        if lic is not None and lic.get("type") == "expression":
            e["licence"] = lic.text.strip()
        elif lic is not None and lic.get("type") == "file":
            lic_text = read(os.path.join(folder, lic.text.strip()))
            e["licence"] = None
        elif lic_url:
            e["licence"] = None
        else:
            e["licence"] = "(not stated)"
        # a licence file in the package besides the expression
        for f in files_like(folder, LICENCE_FILE):
            if not lic_text:
                lic_text = read(f)
        if e["licence"] is None:
            head = lic_text.lstrip()[:200].upper()
            if "MICROSOFT SOFTWARE LICENSE TERMS" in head:
                e["licence"] = "Microsoft Software License Terms"
                e["licence_where"] = "license.txt in the NuGet package" + (
                    "" if "deprecatelicenseurl" in lic_url.lower() else "; " + lic_url)
                lic_text = ""
            elif lic_text:
                ids = classify(lic_text, [], standards)
                e["licence"] = ids or ("BSD-style licence (text below)" if "Redistribution and use in source and "
                                       "binary forms" in lic_text else "See the licence text below")
            elif lic_url:
                e["licence"] = "See " + lic_url
                if e["microsoft"]:
                    e["licence"] = "Microsoft Software License Terms"
                    e["licence_where"] = lic_url
            else:
                e["licence"] = "(not stated)"
        if pid.lower() in NUGET_NOTES:
            e["used"] = NUGET_NOTES[pid.lower()]
        if os.path.isfile(os.path.join(folder, "NOTICE.txt")) and e["microsoft"]:
            e["used"] = ((e.get("used") or "") + " Microsoft's notices for the third-party code in it are in "
                         "NOTICE.txt in the NuGet package.").strip()
        if not e["copyright"] and (meta.findtext("authors") or "").strip():
            e["copyright"] = ["Authors: " + meta.findtext("authors").strip()]
        if e["microsoft"] and (lic is None or lic.get("type") == "expression" or
                               e["licence"] == "Microsoft Software License Terms"):
            # Microsoft's own: the licence is named, its text is not copied (the standard text, for MIT, is at the end)
            if e["licence"] in standards:
                e["standard"] = [e["licence"]]
        else:
            if lic_text and not e["copyright"]:
                e["copyright"] = copyright_lines(lic_text)
            elif lic_text:
                extra = [c for c in copyright_lines(lic_text) if c not in e["copyright"]]
                e["copyright"] += extra
            settle_text(e, [("licence", lic_text)], standards, problems, f"NuGet {pid} {e['version']}")
        entries.append(e)
    return entries

# -- writing ---------------------------------------------------------------------------------------------------------


def natural(s):
    return [(0, int(p), "") if p.isdigit() else (1, 0, p) for p in re.split(r"(\d+)", s.lower())]


def render(entries, standards):
    rule = "=" * 80
    out = [INTRO.rstrip("\n"), ""]
    used = set()
    for e in entries:
        out += [rule, f"{e['name']} {e['version']}".rstrip(), rule]
        lic = e["licence"]
        out.append("Licence: " + lic + (f" ({e['licence_where']})" if e.get("licence_where") else ""))
        if e.get("url"):
            out.append("Home: " + e["url"])
        if e.get("used"):
            out.append(e["used"])
        if e.get("copyright"):
            out.append("")
            out += e["copyright"]
        if e.get("standard"):
            stds = e["standard"]
            for s in stds:
                used.add(s)
            out += ["", "Licence text: " + "; ".join(f'"{LICENCE_TITLES[s]}" at the end of this file' for s in stds)
                    + "."]
        if e.get("text"):
            out += ["", "Licence text:", "", e["text"].rstrip()]
        if e.get("notice"):
            out += ["", "NOTICE:", "", e["notice"].rstrip()]
        out.append("")
    out += [rule, "LICENCE TEXTS", rule, ""]
    for s in sorted(used, key=lambda k: LICENCE_TITLES[k]):
        out += ["-" * 80, LICENCE_TITLES[s] + f" ({s})", "-" * 80, "", standards[s].rstrip(), ""]
    text = "\n".join(out).rstrip("\n") + "\n"
    return "\n".join(line.rstrip() for line in text.split("\n"))


def build():
    if not os.path.isdir(NODE_MODULES):
        print(f"third-party-notices: {rel(NODE_MODULES)} is missing (npm ci in Dev/Typedown.Editor first)",
              file=sys.stderr)
        sys.exit(2)
    standards = standard_texts()
    problems = []
    entries = npm_entries(standards, problems) + vendored_entries(standards, problems) + \
        nuget_entries(standards, problems)
    entries.sort(key=lambda e: (natural(e["name"]), natural(e["version"])))
    names = {e["name"] for e in entries}
    for must in EXPECTED:
        if must not in names:
            problems.append(f"{must}: expected in the bundle, not found (is node_modules complete?)")
    for e in entries:
        lic = e["licence"]
        alternatives = [a for a in re.split(r"\s+OR\s+", lic.strip("()")) if a]
        if COPYLEFT.search(lic) and len(alternatives) > 1 and any(not COPYLEFT.search(a) for a in alternatives):
            problems.append(f"{e['name']} {e['version']}: dual licence {lic}, used under the non-copyleft one")
        elif COPYLEFT.search(lic):
            problems.append(f"{e['name']} {e['version']}: copyleft licence {lic}")
        elif not e.get("microsoft") and not all(i in PERMISSIVE for i in licence_ids(lic)):
            problems.append(f"{e['name']} {e['version']}: licence to look at: {lic}")
    return render(entries, standards), entries, problems


def main():
    ap = argparse.ArgumentParser(description="Write ThirdPartyNotices.txt (see the module docstring).")
    ap.add_argument("--check", action="store_true", help="exit 1 if ThirdPartyNotices.txt is out of date")
    args = ap.parse_args()
    text, entries, problems = build()
    for p in sorted(set(problems)):
        print("third-party-notices: " + p, file=sys.stderr)
    data = text.encode("utf-8")
    if args.check:
        try:
            with open(OUTPUT, "rb") as f:
                same = f.read() == data
        except OSError:
            same = False
        if not same:
            print(f"third-party-notices: {rel(OUTPUT)} is out of date; run python3 {rel(os.path.abspath(__file__))}",
                  file=sys.stderr)
            return 1
        print(f"third-party-notices: {rel(OUTPUT)} is up to date ({len(entries)} entries)")
        return 0
    with open(OUTPUT, "wb") as f:
        f.write(data)
    print(f"third-party-notices: wrote {rel(OUTPUT)} ({len(entries)} entries, {len(data)} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
