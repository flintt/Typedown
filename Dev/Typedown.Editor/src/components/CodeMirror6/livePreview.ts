import { EditorView, Decoration, DecorationSet, ViewPlugin, ViewUpdate, WidgetType } from "@codemirror/view";
import { syntaxTree } from "@codemirror/language";
import { Range, StateField, EditorState, Extension } from "@codemirror/state";
import katex from "katex";
import "katex/dist/katex.min.css";

// The Live Preview pipeline: render Markdown in place, hide its markers, reveal the source when the caret is
// on the element. Two parts, because CodeMirror forbids multi-line replace decorations from a view plugin:
//   - an inline ViewPlugin (viewport-limited, fast) for marks and single-line replacements;
//   - a block StateField for the things that span line breaks: tables, mermaid, multi-line $$ math.

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
    toDOM() { const c = document.createElement("input"); c.type = "checkbox"; c.checked = this.checked; c.className = "cm-md-task"; c.disabled = true; return c; }
}
class TableWidget extends WidgetType {
    constructor(readonly src: string) { super(); }
    eq(o: TableWidget) { return o.src === this.src; }
    toDOM() {
        const wrap = document.createElement("div");
        wrap.className = "cm-md-table";
        const rows = this.src.split("\n").filter((l) => l.trim().length);
        const cells = (l: string) => l.replace(/^\s*\|?|\|?\s*$/g, "").split("|").map((c) => c.trim());
        const aligns = rows[1] ? cells(rows[1]).map((d) => (/^:-+:$/.test(d) ? "center" : /-+:$/.test(d) ? "right" : /^:-+/.test(d) ? "left" : "")) : [];
        const table = document.createElement("table");
        rows.forEach((line, i) => {
            if (i === 1) return;
            const tr = document.createElement("tr");
            cells(line).forEach((txt, ci) => {
                const cell = document.createElement(i === 0 ? "th" : "td");
                cell.textContent = txt;
                if (aligns[ci]) cell.style.textAlign = aligns[ci];
                tr.appendChild(cell);
            });
            table.appendChild(tr);
        });
        wrap.appendChild(table);
        return wrap;
    }
}
class MermaidWidget extends WidgetType {
    constructor(readonly code: string) { super(); }
    eq(o: MermaidWidget) { return o.code === this.code; }
    toDOM() {
        const div = document.createElement("div");
        div.className = "cm-md-mermaid";
        const mermaid = (window as any).mermaid;
        if (mermaid?.render) {
            try {
                const id = "mmd" + Math.random().toString(36).slice(2);
                Promise.resolve(mermaid.render(id, this.code)).then((r: any) => { div.innerHTML = typeof r === "string" ? r : r?.svg || ""; }).catch(() => { div.textContent = this.code; });
            } catch { div.textContent = this.code; }
        } else div.textContent = this.code;
        return div;
    }
    ignoreEvent() { return false; }
}

const overlaps = (state: EditorState, reveal: boolean, from: number, to: number) =>
    reveal && state.selection.ranges.some((r) => r.from <= to && r.to >= from);

// ---- inline / single-line decorations (view plugin, viewport limited) ----
function buildInline(view: EditorView, reveal: boolean): DecorationSet {
    const deco: Range<Decoration>[] = [];
    const state = view.state;
    const on = (f: number, t: number) => overlaps(state, reveal, f, t);
    const onLine = (pos: number) => { const l = state.doc.lineAt(pos); return on(l.from, l.to); };
    const protectedRanges: { from: number; to: number }[] = [];
    const addLineClass = (from: number, to: number, cls: string) => {
        let pos = from;
        while (pos <= to) { const line = state.doc.lineAt(pos); deco.push(Decoration.line({ class: cls }).range(line.from)); if (line.to + 1 > to) break; pos = line.to + 1; }
    };
    for (const { from, to } of view.visibleRanges) {
        syntaxTree(state).iterate({
            from, to,
            enter: (node) => {
                const name = node.name;
                const hClass = headingClass(name);
                if (hClass) deco.push(Decoration.mark({ class: `cm-md-heading ${hClass}` }).range(node.from, node.to));
                else if (name === "StrongEmphasis") deco.push(Decoration.mark({ class: "cm-md-strong" }).range(node.from, node.to));
                else if (name === "Emphasis") deco.push(Decoration.mark({ class: "cm-md-em" }).range(node.from, node.to));
                else if (name === "Strikethrough") deco.push(Decoration.mark({ class: "cm-md-strike" }).range(node.from, node.to));
                else if (name === "InlineCode") { deco.push(Decoration.mark({ class: "cm-md-code" }).range(node.from, node.to)); protectedRanges.push({ from: node.from, to: node.to }); }
                else if (name === "Link") deco.push(Decoration.mark({ class: "cm-md-link" }).range(node.from, node.to));

                if (name === "Blockquote") addLineClass(node.from, node.to, "cm-md-quote");
                else if (name === "FencedCode") {
                    protectedRanges.push({ from: node.from, to: node.to });
                    const first = state.doc.lineAt(node.from);
                    const info = first.text.replace(/^\s*(`{3,}|~{3,})/, "").trim().toLowerCase();
                    if (info !== "mermaid") {
                        addLineClass(node.from, node.to, "cm-md-codeblock");
                        node.node.getChildren("CodeMark").forEach((mk: any) => { if (!on(node.from, node.to)) { const l = state.doc.lineAt(mk.from); deco.push(Decoration.replace({}).range(l.from, l.to)); } });
                    }
                } else if (name === "HorizontalRule") {
                    if (!on(node.from, node.to)) deco.push(Decoration.replace({ widget: new HrWidget() }).range(node.from, node.to));
                } else if (name === "Image") {
                    protectedRanges.push({ from: node.from, to: node.to });
                    if (!on(node.from, node.to)) { const m = /!\[([^\]]*)\]\(([^)\s]+)/.exec(state.doc.sliceString(node.from, node.to)); if (m) deco.push(Decoration.replace({ widget: new ImageWidget(m[2], m[1]) }).range(node.from, node.to)); }
                } else if (name === "TaskMarker") {
                    if (!onLine(node.from)) { const checked = /x/i.test(state.doc.sliceString(node.from, node.to)); deco.push(Decoration.replace({ widget: new CheckboxWidget(checked) }).range(node.from, node.to)); }
                }

                if (name === "QuoteMark") {
                    if (!onLine(node.from)) { const line = state.doc.lineAt(node.to); const rest = line.text.slice(node.to - line.from); const ws = rest.length && rest[0] === " " ? 1 : 0; deco.push(Decoration.replace({}).range(node.from, node.to + ws)); }
                } else if (name === "EmphasisMark" || name === "StrikethroughMark" || name === "LinkMark") {
                    const p = node.node.parent; if (p && !on(p.from, p.to)) deco.push(Decoration.replace({}).range(node.from, node.to));
                } else if (name === "CodeMark") {
                    const p = node.node.parent; if (p && p.name === "InlineCode" && !on(p.from, p.to)) deco.push(Decoration.replace({}).range(node.from, node.to));
                } else if (name === "HeaderMark") {
                    const p = node.node.parent; const pf = p ? p.from : node.from, pt = p ? p.to : node.to;
                    if (!on(pf, pt)) { const line = state.doc.lineAt(node.to); const rest = line.text.slice(node.to - line.from); const end = node.to + (rest.length - rest.trimStart().length); if (end > node.from) deco.push(Decoration.replace({}).range(node.from, end)); }
                } else if (name === "URL") {
                    const p = node.node.parent; if (p && p.name === "Link" && !on(p.from, p.to)) deco.push(Decoration.replace({}).range(node.from, node.to));
                }
            },
        });
    }
    const inProt = (f: number, t: number) => protectedRanges.some((r) => r.from < t && r.to > f);
    for (const { from, to } of view.visibleRanges) {
        const text = state.doc.sliceString(from, to);
        for (const m of text.matchAll(/==([^=\n]+)==/g)) {
            const s = from + (m.index || 0), e = s + m[0].length; if (inProt(s, e)) continue;
            deco.push(Decoration.mark({ class: "cm-md-highlight" }).range(s + 2, e - 2));
            if (!on(s, e)) { deco.push(Decoration.replace({}).range(s, s + 2)); deco.push(Decoration.replace({}).range(e - 2, e)); }
        }
        for (const m of text.matchAll(/\$\$([^\n]+?)\$\$/g)) {
            const s = from + (m.index || 0), e = s + m[0].length; if (inProt(s, e) || on(s, e)) continue;
            deco.push(Decoration.replace({ widget: new MathWidget(m[1].trim(), true) }).range(s, e));
            protectedRanges.push({ from: s, to: e });
        }
        for (const m of text.matchAll(/(?<![$\\])\$([^$\n]+?)\$(?!\$)/g)) {
            const s = from + (m.index || 0), e = s + m[0].length; if (inProt(s, e) || on(s, e)) continue;
            deco.push(Decoration.replace({ widget: new MathWidget(m[1], false) }).range(s, e));
        }
    }
    deco.sort((a, b) => a.from - b.from || a.value.startSide - b.value.startSide);
    return Decoration.set(deco, true);
}

// ---- block / multi-line decorations (state field, whole document) ----
function buildBlock(state: EditorState, reveal: boolean): DecorationSet {
    const deco: Range<Decoration>[] = [];
    const on = (f: number, t: number) => overlaps(state, reveal, f, t);
    syntaxTree(state).iterate({
        enter: (node) => {
            if (node.name === "Table") {
                if (!on(node.from, node.to)) deco.push(Decoration.replace({ widget: new TableWidget(state.doc.sliceString(node.from, node.to)), block: true }).range(node.from, node.to));
            } else if (node.name === "FencedCode") {
                const first = state.doc.lineAt(node.from);
                const info = first.text.replace(/^\s*(`{3,}|~{3,})/, "").trim().toLowerCase();
                if (info === "mermaid" && !on(node.from, node.to)) {
                    const inner = state.doc.sliceString(first.to + 1, state.doc.lineAt(node.to).from - 1);
                    deco.push(Decoration.replace({ widget: new MermaidWidget(inner), block: true }).range(node.from, node.to));
                }
            }
        },
    });
    const text = state.doc.toString();
    for (const m of text.matchAll(/\$\$([\s\S]+?)\$\$/g)) {
        const s = m.index || 0, e = s + m[0].length;
        if (!m[0].includes("\n")) continue; // single-line handled inline
        if (on(s, e)) continue;
        deco.push(Decoration.replace({ widget: new MathWidget(m[1].trim(), true), block: true }).range(s, e));
    }
    deco.sort((a, b) => a.from - b.from || a.value.startSide - b.value.startSide);
    return Decoration.set(deco, true);
}

export function livePreview(reveal: boolean): Extension {
    const inline = ViewPlugin.fromClass(
        class {
            decorations: DecorationSet;
            constructor(view: EditorView) { this.decorations = buildInline(view, reveal); }
            update(u: ViewUpdate) { if (u.docChanged || u.viewportChanged || u.selectionSet) this.decorations = buildInline(u.view, reveal); }
        },
        {
            decorations: (v) => v.decorations,
            provide: (plugin) => EditorView.atomicRanges.of((view) => view.plugin(plugin)?.decorations || Decoration.none),
        }
    );
    const block = StateField.define<DecorationSet>({
        create: (state) => buildBlock(state, reveal),
        update: (deco, tr) => (tr.docChanged || tr.selection ? buildBlock(tr.state, reveal) : deco.map(tr.changes)),
        provide: (f) => EditorView.decorations.from(f),
    });
    return [inline, block];
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
    ".cm-md-math-block": { display: "block", textAlign: "center", padding: "6px 0" },
    ".cm-md-table": { display: "block", overflowX: "auto", margin: "6px 0" },
    ".cm-md-table table": { borderCollapse: "collapse", width: "auto" },
    ".cm-md-table th, .cm-md-table td": { border: "1px solid var(--tableBorderColor, #d0d0d0)", padding: "4px 10px" },
    ".cm-md-table th": { backgroundColor: "var(--itemBgColor, #f8f8f8)", fontWeight: "bold" },
    ".cm-md-mermaid": { display: "block", textAlign: "center", padding: "8px 0" },
    ".cm-md-hr": { border: "none", borderTop: "1px solid var(--tableBorderColor, #ddd)", margin: "2px 0" },
    ".cm-md-task": { verticalAlign: "middle", marginRight: "4px" },
    ".cm-md-quote": { borderLeft: "3px solid var(--tableBorderColor, #ddd)", paddingLeft: "12px", color: "var(--editorColor60, rgba(0,0,0,.6))" },
    ".cm-md-codeblock": { backgroundColor: "var(--codeBlockBgColor, rgba(0,0,0,.04))", fontFamily: "monospace" },
    ".cm-md-heading": { fontWeight: "bold", lineHeight: "1.3" },
    ".cm-md-h1": { fontSize: "1.8em" }, ".cm-md-h2": { fontSize: "1.5em" }, ".cm-md-h3": { fontSize: "1.3em" },
    ".cm-md-h4": { fontSize: "1.15em" }, ".cm-md-h5": { fontSize: "1.05em" }, ".cm-md-h6": { fontSize: "1em", opacity: "0.85" },
});
