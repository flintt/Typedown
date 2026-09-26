import React, { useEffect, useRef } from "react";
import { Crepe } from "@milkdown/crepe";
import "@milkdown/crepe/theme/common/style.css";
import "@milkdown/crepe/theme/frame.css";

// Feasibility prototype: a true-WYSIWYG editor on ProseMirror (via Milkdown's Crepe), to compare its feel
// against Muya and the CodeMirror-6 branch. Same host wiring as the other editors (markdown in/out, the
// FileLoaded handshake, a basic word-count/outline), enough to type in it and judge the experience.

interface IMilkdown {
    markdown: string;
    cursor?: any;
    contentVersion?: number;
    options: any;
    searchOpen?: number;
    searchArg?: any;
    scrollTopRef: React.MutableRefObject<number>;
    scrollFromHostRef?: React.MutableRefObject<boolean>;
    flushRef?: React.MutableRefObject<any>;
    onMarkdownChange: (markdown: string) => void;
    onContentApplied?: () => void;
    onCursorChange?: (cursor: any) => void;
    onStateChange: (state: any) => void;
    onOutlineCurrent?: (slug: string, where: any) => void;
    onSearchArgChange?: (arg: any) => void;
}

function reportState(md: string, onStateChange: (s: any) => void) {
    const wordCount = { character: md.length, word: md.split(/\s+/).filter(Boolean).length };
    const toc: any[] = [];
    let pos = 0;
    for (const line of md.split("\n")) {
        const m = /^ {0,3}(#{1,6})\s+(.*)$/.exec(line);
        if (m) toc.push({ content: m[2].trim(), lvl: m[1].length, slug: `h${pos}` });
        pos++;
    }
    onStateChange({ wordCount, toc, cur: toc[0] });
}

const MilkdownEditor: React.FC<IMilkdown> = (props) => {
    const hostRef = useRef<HTMLDivElement>(null);
    const crepeRef = useRef<Crepe | null>(null);
    const mdRef = useRef<string>("");
    const propsRef = useRef(props);
    propsRef.current = props;

    // (Re)create the editor whenever the host pushes new content (contentVersion), like the other editors.
    useEffect(() => {
        if (!hostRef.current) return;
        const initial = props.markdown ?? "";
        mdRef.current = initial;
        const crepe = new Crepe({ root: hostRef.current, defaultValue: initial });
        crepe.setReadonly(!!props.options?.readOnly);
        crepe.on((api: any) => {
            api.markdownUpdated((_ctx: any, md: string) => {
                mdRef.current = md;
                propsRef.current.onMarkdownChange(md);
                reportState(md, propsRef.current.onStateChange);
            });
        });
        crepe.create().then(() => {
            crepeRef.current = crepe;
            // Arm the FileLoaded handshake, then report the initial state.
            propsRef.current.onContentApplied?.();
            reportState(mdRef.current, propsRef.current.onStateChange);
        });
        return () => {
            crepe.destroy();
            crepeRef.current = null;
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.contentVersion]);

    useEffect(() => {
        crepeRef.current?.setReadonly(!!props.options?.readOnly);
    }, [props.options?.readOnly]);

    return <div ref={hostRef} style={{ height: "100%", overflow: "auto" }} className="milkdown-host" />;
};

export default MilkdownEditor;
