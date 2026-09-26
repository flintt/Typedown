import { EditorView, Decoration, DecorationSet, ViewPlugin, ViewUpdate } from "@codemirror/view";
import { syntaxTree } from "@codemirror/language";
import { Range } from "@codemirror/state";

// M2 of the CM6 migration: the Live Preview pipeline. A ViewPlugin walks the Lezer syntax tree over the
// visible ranges and produces two kinds of decoration:
//   - mark decorations that style the rendered text (bold, italic, code, link, heading size);
//   - replace decorations that hide the Markdown markers (**, `, #, []() ) so you see the finished look.
// The markers are revealed again when the caret is on that element, so you edit the real source in place —
// this is the "source + decorations" model (Obsidian's Live Preview), not a separate rendered DOM.
//
// Modes: source = this plugin absent (plain highlighted Markdown). live = plugin present, reveal-on-caret.
// reading = plugin present with reveal forced off (everything always rendered), the host sets readOnly.

// The container elements we style, and the marker child names to hide inside them.
const HIDE_MARKS = new Set(["EmphasisMark", "CodeMark", "StrikethroughMark", "LinkMark", "HeaderMark"]);

function headingClass(name: string): string | null {
    const m = /^ATXHeading([1-6])$/.exec(name);
    return m ? `cm-md-h${m[1]}` : null;
}

function buildDecorations(view: EditorView, reveal: boolean): DecorationSet {
    const widgets: Range<Decoration>[] = [];
    const sel = view.state.selection.ranges;
    const cursorOn = (from: number, to: number) =>
        reveal && sel.some((r) => r.from <= to && r.to >= from);

    for (const { from, to } of view.visibleRanges) {
        syntaxTree(view.state).iterate({
            from,
            to,
            enter: (node) => {
                const name = node.name;
                // Style the rendered text of a container element.
                const hClass = headingClass(name);
                if (hClass) {
                    widgets.push(Decoration.mark({ class: `cm-md-heading ${hClass}` }).range(node.from, node.to));
                } else if (name === "StrongEmphasis") {
                    widgets.push(Decoration.mark({ class: "cm-md-strong" }).range(node.from, node.to));
                } else if (name === "Emphasis") {
                    widgets.push(Decoration.mark({ class: "cm-md-em" }).range(node.from, node.to));
                } else if (name === "Strikethrough") {
                    widgets.push(Decoration.mark({ class: "cm-md-strike" }).range(node.from, node.to));
                } else if (name === "InlineCode") {
                    widgets.push(Decoration.mark({ class: "cm-md-code" }).range(node.from, node.to));
                } else if (name === "Link") {
                    widgets.push(Decoration.mark({ class: "cm-md-link" }).range(node.from, node.to));
                }

                // Hide the syntax markers, unless the caret is on their enclosing element.
                if (HIDE_MARKS.has(name)) {
                    const parent = node.node.parent;
                    const pf = parent ? parent.from : node.from;
                    const pt = parent ? parent.to : node.to;
                    if (!cursorOn(pf, pt)) {
                        let end = node.to;
                        if (name === "HeaderMark") {
                            // Also eat the space(s) after the # so the heading text starts at the margin.
                            const line = view.state.doc.lineAt(node.to);
                            const rest = line.text.slice(node.to - line.from);
                            const ws = rest.length - rest.trimStart().length;
                            end = node.to + ws;
                        }
                        if (end > node.from) widgets.push(Decoration.replace({}).range(node.from, end));
                    }
                }

                // Hide the URL/target part of a link: [text](url) -> text. Reveal on caret like the marks.
                if (name === "URL") {
                    const parent = node.node.parent;
                    const pf = parent ? parent.from : node.from;
                    const pt = parent ? parent.to : node.to;
                    if (parent && parent.name === "Link" && !cursorOn(pf, pt)) {
                        widgets.push(Decoration.replace({}).range(node.from, node.to));
                    }
                }
            },
        });
    }
    // Decorations must be sorted by (from, startSide); replace before mark at the same point.
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
                if (u.docChanged || u.viewportChanged || u.selectionSet)
                    this.decorations = buildDecorations(u.view, getReveal());
            }
        },
        {
            decorations: (v) => v.decorations,
            // A replace decoration that spans a caret position could swallow it; atomicRanges keeps the
            // caret from getting stuck inside a hidden marker.
            provide: (plugin) =>
                EditorView.atomicRanges.of((view) => view.plugin(plugin)?.decorations || Decoration.none),
        }
    );
}

// Styles for the rendered look; colours follow the app theme's CSS variables.
export const livePreviewTheme = EditorView.theme({
    ".cm-md-strong": { fontWeight: "bold" },
    ".cm-md-em": { fontStyle: "italic" },
    ".cm-md-strike": { textDecoration: "line-through" },
    ".cm-md-code": {
        backgroundColor: "var(--inlineCodeBgColor, rgba(0,0,0,.075))",
        borderRadius: "3px",
        padding: "0 3px",
        fontFamily: "monospace",
    },
    ".cm-md-link": { color: "var(--themeColor, #2f6fed)", textDecoration: "underline", cursor: "pointer" },
    ".cm-md-heading": { fontWeight: "bold", lineHeight: "1.3" },
    ".cm-md-h1": { fontSize: "1.8em" },
    ".cm-md-h2": { fontSize: "1.5em" },
    ".cm-md-h3": { fontSize: "1.3em" },
    ".cm-md-h4": { fontSize: "1.15em" },
    ".cm-md-h5": { fontSize: "1.05em" },
    ".cm-md-h6": { fontSize: "1em", opacity: "0.85" },
});
