import { EditorView, Decoration, DecorationSet, ViewPlugin, ViewUpdate, WidgetType } from "@codemirror/view";
import { syntaxTree } from "@codemirror/language";
import { Range } from "@codemirror/state";
import katex from "katex";
import "katex/dist/katex.min.css";

// The Live Preview pipeline. A ViewPlugin walks the Lezer syntax tree over the visible ranges and produces
// mark/line/replace decorations that render Markdown in place and hide its markers, revealing them as source
// when the caret is on the element. A regex pass covers what Lezer does not parse (==highlight==, $math$).
//   M2: infra + inline (bold/italic/code/link/heading). M3: strikethrough/highlight/image/inline-math.
//   M4a: blockquote, horizontal rule, fenced code, task checkbox.

function headingClass(name: string): string | null {
    const m = /^ATXHeading([1-6])$/.exec(name);
    return m ? `cm-md-h${m[1]}` : null;
}

class ImageWidget extends WidgetType {
    constructor(readonly url: string, readonly alt: string) { super(); }
    eq(o: ImageWidget) { return o.url === this.url && o.alt === this.alt; }
    toDOM() { const i = document.createElement("img"); i.src = this.url; i.alt = this.alt; i.className = "cm-md-image"; return i; }
}
class MathWidget extends WidgetType {
    constructor(readonly tex: string, readonly display = false) { super(); }
    eq(o: MathWidget) { return o.tex === this.tex && o.display === this.display; }
    toDOM() {
        const s = document.createElement(this.display ? "div" : "span");
        s.className = this.display ? "cm-md-math cm-md-math-block" : "cm-md-math";
        try { s.innerHTML = katex.renderToString(this.tex, { throwOnError: false, displayMode: this.display }); }
        catch { s.textContent = this.tex; }
        return s;
    }
    ignoreEvent() { return false; }
}
class HrWidget extends WidgetType {
    toDOM() { const h = document.createElement("hr"); h.className = "cm-md-hr"; return h; }
}
class CheckboxWidget extends WidgetType {
    constructor(readonly checked: boolean) { super(); }
    eq(o: CheckboxWidget) { return o.checked === this.checked; }
    toDOM() {
        const c = document.createElement("input");
        c.type = "checkbox"; c.checked = this.checked; c.className = "cm-md-task"; c.disabled = true;
        return c;
    }
}

function buildDecorations(view: EditorView, reveal: boolean): DecorationSet {
    const deco: Range<Decoration>[] = [];
    const sel = view.state.selection.ranges;
    const on = (from: number, to: number) => reveal && sel.some((r) => r.from <= to && r.to >= from);
    const onLine = (pos: number) => {
        const l = view.state.doc.lineAt(pos);
        return on(l.from, l.to);
    };
    const protectedRanges: { from: number; to: number }[] = [];
    const addLineClass = (from: number, to: number, cls: string) => {
        let pos = from;
        while (pos <= to) {
            const line = view.state.doc.lineAt(pos);
            deco.push(Decoration.line({ class: cls }).range(line.from));
            if (line.to + 1 > to) break;
            pos = line.to + 1;
        }
    };

    for (const { from, to } of view.visibleRanges) {
        syntaxTree(view.state).iterate({
            from, to,
            enter: (node) => {
                const name = node.name;
                // ---- inline styling ----
                const hClass = headingClass(name);
                if (hClass) deco.push(Decoration.mark({ class: `cm-md-heading ${hClass}` }).range(node.from, node.to));
                else if (name === "StrongEmphasis") deco.push(Decoration.mark({ class: "cm-md-strong" }).range(node.from, node.to));
                else if (name === "Emphasis") deco.push(Decoration.mark({ class: "cm-md-em" }).range(node.from, node.to));
                else if (name === "Strikethrough") deco.push(Decoration.mark({ class: "cm-md-strike" }).range(node.from, node.to));
                else if (name === "InlineCode") { deco.push(Decoration.mark({ class: "cm-md-code" }).range(node.from, node.to)); protectedRanges.push({ from: node.from, to: node.to }); }
                else if (name === "Link") deco.push(Decoration.mark({ class: "cm-md-link" }).range(node.from, node.to));

                // ---- block elements ----
                if (name === "Blockquote") {
                    addLineClass(node.from, node.to, "cm-md-quote");
                } else if (name === "FencedCode") {
                    addLineClass(node.from, node.to, "cm-md-codeblock");
                    protectedRanges.push({ from: node.from, to: node.to });
                } else if (name === "HorizontalRule") {
                    if (!on(node.from, node.to)) deco.push(Decoration.replace({ widget: new HrWidget(), block: false }).range(node.from, node.to));
                } else if (name === "Image") {
                    protectedRanges.push({ from: node.from, to: node.to });
                    if (!on(node.from, node.to)) {
                        const m = /!\[([^\]]*)\]\(([^)\s]+)/.exec(view.state.doc.sliceString(node.from, node.to));
                        if (m) deco.push(Decoration.replace({ widget: new ImageWidget(m[2], m[1]) }).range(node.from, node.to));
                    }
                } else if (name === "TaskMarker") {
                    if (!onLine(node.from)) {
                        const checked = /x/i.test(view.state.doc.sliceString(node.from, node.to));
                        deco.push(Decoration.replace({ widget: new CheckboxWidget(checked) }).range(node.from, node.to));
                    }
                }

                // ---- markers to hide ----
                if (name === "QuoteMark") {
                    if (!onLine(node.from)) {
                        // hide '>' plus a following space
                        const line = view.state.doc.lineAt(node.to);
                        const rest = line.text.slice(node.to - line.from);
                        const ws = rest.length && rest[0] === " " ? 1 : 0;
                        deco.push(Decoration.replace({}).range(node.from, node.to + ws));
                    }
                } else if (name === "EmphasisMark" || name === "StrikethroughMark" || name === "LinkMark") {
                    const p = node.node.parent;
                    if (p && !on(p.from, p.to)) deco.push(Decoration.replace({}).range(node.from, node.to));
                } else if (name === "CodeMark") {
                    const p = node.node.parent;
                    if (p && (p.name === "InlineCode" || p.name === "FencedCode") && !on(p.from, p.to)) {
                        // For a fence the ``` sits on its own line; hide the whole line so no blank fence remains.
                        if (p.name === "FencedCode") {
                            const line = view.state.doc.lineAt(node.from);
                            deco.push(Decoration.replace({}).range(line.from, line.to));
                        } else deco.push(Decoration.replace({}).range(node.from, node.to));
                    }
                } else if (name === "HeaderMark") {
                    const p = node.node.parent;
                    const pf = p ? p.from : node.from, pt = p ? p.to : node.to;
                    if (!on(pf, pt)) {
                        const line = view.state.doc.lineAt(node.to);
                        const rest = line.text.slice(node.to - line.from);
                        const end = node.to + (rest.length - rest.trimStart().length);
                        if (end > node.from) deco.push(Decoration.replace({}).range(node.from, end));
                    }
                } else if (name === "URL") {
                    const p = node.node.parent;
                    if (p && p.name === "Link" && !on(p.from, p.to)) deco.push(Decoration.replace({}).range(node.from, node.to));
                }
            },
        });
    }

    // Regex pass for constructs Lezer does not parse. Skip protected (code/image) spans.
    const inProt = (from: number, to: number) => protectedRanges.some((r) => r.from < to && r.to > from);
    for (const { from, to } of view.visibleRanges) {
        const text = view.state.doc.sliceString(from, to);
        for (const m of text.matchAll(/==([^=\n]+)==/g)) {
            const s = from + (m.index || 0), e = s + m[0].length;
            if (inProt(s, e)) continue;
            deco.push(Decoration.mark({ class: "cm-md-highlight" }).range(s + 2, e - 2));
            if (!on(s, e)) { deco.push(Decoration.replace({}).range(s, s + 2)); deco.push(Decoration.replace({}).range(e - 2, e)); }
        }
        for (const m of text.matchAll(/(?<![$\\])\$([^$\n]+?)\$(?!\$)/g)) {
            const s = from + (m.index || 0), e = s + m[0].length;
            if (inProt(s, e) || on(s, e)) continue;
            deco.push(Decoration.replace({ widget: new MathWidget(m[1], false) }).range(s, e));
        }
    }

    deco.sort((a, b) => a.from - b.from || a.value.startSide - b.value.startSide);
    return Decoration.set(deco, true);
}

export function livePreview(getReveal: () => boolean) {
    return ViewPlugin.fromClass(
        class {
            decorations: DecorationSet;
            constructor(view: EditorView) { this.decorations = buildDecorations(view, getReveal()); }
            update(u: ViewUpdate) { if (u.docChanged || u.viewportChanged || u.selectionSet) this.decorations = buildDecorations(u.view, getReveal()); }
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
    ".cm-md-math-block": { textAlign: "center", padding: "6px 0" },
    ".cm-md-hr": { border: "none", borderTop: "1px solid var(--tableBorderColor, #ddd)", margin: "2px 0" },
    ".cm-md-task": { verticalAlign: "middle", marginRight: "4px" },
    ".cm-md-quote": { borderLeft: "3px solid var(--tableBorderColor, #ddd)", paddingLeft: "12px", color: "var(--editorColor60, rgba(0,0,0,.6))" },
    ".cm-md-codeblock": { backgroundColor: "var(--codeBlockBgColor, rgba(0,0,0,.04))", fontFamily: "monospace" },
    ".cm-md-heading": { fontWeight: "bold", lineHeight: "1.3" },
    ".cm-md-h1": { fontSize: "1.8em" }, ".cm-md-h2": { fontSize: "1.5em" }, ".cm-md-h3": { fontSize: "1.3em" },
    ".cm-md-h4": { fontSize: "1.15em" }, ".cm-md-h5": { fontSize: "1.05em" }, ".cm-md-h6": { fontSize: "1em", opacity: "0.85" },
});
