import { EditorView, Decoration, DecorationSet, ViewPlugin, ViewUpdate, WidgetType } from "@codemirror/view";
import { syntaxTree } from "@codemirror/language";
import { Range } from "@codemirror/state";
import katex from "katex";
import "katex/dist/katex.min.css";

// The Live Preview pipeline (M2 infra, M3 inline elements). A ViewPlugin walks the Lezer syntax tree over the
// visible ranges and produces mark decorations (styling) plus replace decorations (hiding the Markdown
// markers). A supplementary regex pass covers what CommonMark/GFM do not parse: ==highlight== and $inline
// math$. Markers/widgets are revealed as plain source when the caret is on them (reveal), so you edit in place.

const HIDE_MARKS = new Set(["EmphasisMark", "CodeMark", "StrikethroughMark", "LinkMark", "HeaderMark"]);

function headingClass(name: string): string | null {
    const m = /^ATXHeading([1-6])$/.exec(name);
    return m ? `cm-md-h${m[1]}` : null;
}

class ImageWidget extends WidgetType {
    constructor(readonly url: string, readonly alt: string) {
        super();
    }
    eq(o: ImageWidget) {
        return o.url === this.url && o.alt === this.alt;
    }
    toDOM() {
        const img = document.createElement("img");
        img.src = this.url;
        img.alt = this.alt;
        img.className = "cm-md-image";
        return img;
    }
}

class MathWidget extends WidgetType {
    constructor(readonly tex: string) {
        super();
    }
    eq(o: MathWidget) {
        return o.tex === this.tex;
    }
    toDOM() {
        const span = document.createElement("span");
        span.className = "cm-md-math";
        try {
            span.innerHTML = katex.renderToString(this.tex, { throwOnError: false, displayMode: false });
        } catch {
            span.textContent = this.tex;
        }
        return span;
    }
    ignoreEvent() {
        return false;
    }
}

function buildDecorations(view: EditorView, reveal: boolean): DecorationSet {
    const widgets: Range<Decoration>[] = [];
    const sel = view.state.selection.ranges;
    const cursorOn = (from: number, to: number) => reveal && sel.some((r) => r.from <= to && r.to >= from);
    const protectedRanges: { from: number; to: number }[] = []; // code/image spans the regex pass must skip

    for (const { from, to } of view.visibleRanges) {
        syntaxTree(view.state).iterate({
            from,
            to,
            enter: (node) => {
                const name = node.name;
                const hClass = headingClass(name);
                if (hClass) widgets.push(Decoration.mark({ class: `cm-md-heading ${hClass}` }).range(node.from, node.to));
                else if (name === "StrongEmphasis") widgets.push(Decoration.mark({ class: "cm-md-strong" }).range(node.from, node.to));
                else if (name === "Emphasis") widgets.push(Decoration.mark({ class: "cm-md-em" }).range(node.from, node.to));
                else if (name === "Strikethrough") widgets.push(Decoration.mark({ class: "cm-md-strike" }).range(node.from, node.to));
                else if (name === "InlineCode") {
                    widgets.push(Decoration.mark({ class: "cm-md-code" }).range(node.from, node.to));
                    protectedRanges.push({ from: node.from, to: node.to });
                } else if (name === "Link") widgets.push(Decoration.mark({ class: "cm-md-link" }).range(node.from, node.to));

                // Image: [alt](url) or ![alt](url) -> an <img> widget, revealed as source on the caret.
                if (name === "Image") {
                    protectedRanges.push({ from: node.from, to: node.to });
                    if (!cursorOn(node.from, node.to)) {
                        const raw = view.state.doc.sliceString(node.from, node.to);
                        const m = /!\[([^\]]*)\]\(([^)\s]+)/.exec(raw);
                        if (m) widgets.push(Decoration.replace({ widget: new ImageWidget(m[2], m[1]), block: false }).range(node.from, node.to));
                    }
                }

                if (HIDE_MARKS.has(name)) {
                    const p = node.node.parent;
                    const pf = p ? p.from : node.from;
                    const pt = p ? p.to : node.to;
                    if (!cursorOn(pf, pt)) {
                        let end = node.to;
                        if (name === "HeaderMark") {
                            const line = view.state.doc.lineAt(node.to);
                            const rest = line.text.slice(node.to - line.from);
                            end = node.to + (rest.length - rest.trimStart().length);
                        }
                        if (end > node.from) widgets.push(Decoration.replace({}).range(node.from, end));
                    }
                }

                if (name === "URL") {
                    const p = node.node.parent;
                    if (p && p.name === "Link" && !cursorOn(p.from, p.to)) widgets.push(Decoration.replace({}).range(node.from, node.to));
                }
            },
        });
    }

    // Regex pass for constructs Lezer does not parse. Skip anything inside a protected (code/image) span.
    const inProtected = (from: number, to: number) => protectedRanges.some((r) => r.from < to && r.to > from);
    for (const { from, to } of view.visibleRanges) {
        const text = view.state.doc.sliceString(from, to);
        // ==highlight==
        for (const m of text.matchAll(/==([^=\n]+)==/g)) {
            const s = from + (m.index || 0);
            const e = s + m[0].length;
            if (inProtected(s, e)) continue;
            widgets.push(Decoration.mark({ class: "cm-md-highlight" }).range(s + 2, e - 2));
            if (!cursorOn(s, e)) {
                widgets.push(Decoration.replace({}).range(s, s + 2));
                widgets.push(Decoration.replace({}).range(e - 2, e));
            }
        }
        // $inline math$  (single-line, not $$)
        for (const m of text.matchAll(/(?<![$\\])\$([^$\n]+?)\$(?!\$)/g)) {
            const s = from + (m.index || 0);
            const e = s + m[0].length;
            if (inProtected(s, e)) continue;
            if (cursorOn(s, e)) continue; // show source while editing
            widgets.push(Decoration.replace({ widget: new MathWidget(m[1]) }).range(s, e));
        }
    }

    widgets.sort((a, b) => a.from - b.from || a.value.startSide - b.value.startSide);
    return Decoration.set(widgets, true);
}

export function livePreview(getReveal: () => boolean) {
    return ViewPlugin.fromClass(
        class {
            decorations: DecorationSet;
            constructor(view: EditorView) {
                this.decorations = buildDecorations(view, getReveal());
            }
            update(u: ViewUpdate) {
                if (u.docChanged || u.viewportChanged || u.selectionSet) this.decorations = buildDecorations(u.view, getReveal());
            }
        },
        {
            decorations: (v) => v.decorations,
            provide: (plugin) => EditorView.atomicRanges.of((view) => view.plugin(plugin)?.decorations || Decoration.none),
        }
    );
}

export const livePreviewTheme = EditorView.theme({
    ".cm-md-strong": { fontWeight: "bold" },
    ".cm-md-em": { fontStyle: "italic" },
    ".cm-md-strike": { textDecoration: "line-through" },
    ".cm-md-highlight": { backgroundColor: "var(--highlightColor, rgba(255,235,120,.6))" },
    ".cm-md-code": { backgroundColor: "var(--inlineCodeBgColor, rgba(0,0,0,.075))", borderRadius: "3px", padding: "0 3px", fontFamily: "monospace" },
    ".cm-md-link": { color: "var(--themeColor, #2f6fed)", textDecoration: "underline", cursor: "pointer" },
    ".cm-md-image": { maxWidth: "100%", verticalAlign: "middle" },
    ".cm-md-math": { padding: "0 2px" },
    ".cm-md-heading": { fontWeight: "bold", lineHeight: "1.3" },
    ".cm-md-h1": { fontSize: "1.8em" },
    ".cm-md-h2": { fontSize: "1.5em" },
    ".cm-md-h3": { fontSize: "1.3em" },
    ".cm-md-h4": { fontSize: "1.15em" },
    ".cm-md-h5": { fontSize: "1.05em" },
    ".cm-md-h6": { fontSize: "1em", opacity: "0.85" },
});
