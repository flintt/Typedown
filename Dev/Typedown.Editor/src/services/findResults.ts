import transport from './transport';
import type { FindPreview } from './findPreview';

/**
 * The find bar's search, told to the host: where it stands (`FindState`: how many matches, which one is chosen - the
 * bar's "3 of 85"), and, while the side pane's results list is open (`FindResultsOpen`), the list itself (`FindResults`:
 * the exact count and a preview of each of the first FIND_RESULTS_CAP matches). An entry chosen in the list comes back
 * as `FindActivate`: that match is selected, as a step to it with the bar's arrows would go there.
 *
 * One search: the editor shown (Muya, CodeMirror) lends its own matches (FindProvider); nothing is searched here.
 */

/** The most entries the list is sent; the count stays exact. */
export const FIND_RESULTS_CAP = 500;

/** The list is sent this long after the last change (each letter typed is a search). */
export const FIND_RESULTS_DELAY_MS = 120;

/** An edit to the document while the list is open: the matches are found again once the typing stops this long. */
export const FIND_EDIT_REFRESH_MS = 300;

export interface FindProvider {
    /** Before the previews of one list are asked for (a lookup built once for all of them). */
    begin?(): void;
    /** How many matches the bar's search has now, and the one chosen (-1: none). */
    state(): { total: number, index: number };
    /** The preview of match `i` (0 <= i < total), from the document's text, not from what is drawn. */
    preview(i: number): FindPreview;
    /** Something that finds match `i` again once the matches have changed (a new search, an edit). */
    token(i: number): unknown;
    /** Match `i` of the list sent chosen (`token`: what token(i) gave then, or null): put in the window and selected. */
    activate(i: number, token: unknown): void;
}

export interface FindResultsMessage {
    version: number;
    total: number;
    index: number;
    capped: boolean;
    entries: FindPreview[];
}

let provider: FindProvider | null = null;
let open = false;
let timer: ReturnType<typeof setTimeout> | null = null;
let version = 0;
let lastTotal = -1;
/** The tokens of the lists sent last, by version: an entry clicked in a list a newer one has replaced still finds its match. */
const sent = new Map<number, unknown[]>();

/** The list is open in the host's side pane. */
export const findResultsOpen = () => open;

/** The list as it would be sent now. */
export function buildFindResults(p: FindProvider, cap = FIND_RESULTS_CAP): { message: Omit<FindResultsMessage, 'version'>, tokens: unknown[] } {
    const { total, index } = p.state();
    const count = Math.min(total, cap);
    if (count) p.begin?.();
    const entries: FindPreview[] = [];
    const tokens: unknown[] = [];
    for (let i = 0; i < count; i++) {
        entries.push(p.preview(i));
        tokens.push(p.token(i));
    }
    return { message: { total, index, capped: total > cap, entries }, tokens };
}

function postResults() {
    if (timer) clearTimeout(timer);
    timer = null;
    if (!provider || !open) return;
    const started = performance.now();
    const { message, tokens } = buildFindResults(provider);
    // The same matches as the list sent last (a step with the bar, an entry chosen): nothing new to list - FindState has
    // moved the host's mark already. A list sent again rebuilt the host's list under the reader's click (it flashed).
    const last = sent.get(version);
    if (last && last.length === tokens.length && tokens.every((t, i) => t === last[i]) && message.total === lastTotal) return;
    lastTotal = message.total;
    version++;
    sent.set(version, tokens);
    for (const v of Array.from(sent.keys())) if (v < version - 2) sent.delete(v);
    transport.postMessageNoDiff('FindResults', { version, ...message });
    const log = ((window as any).__typedownFindResultsLog ||= []) as any[];
    log.push({ version, total: message.total, entries: message.entries.length, ms: Math.round(performance.now() - started) });
    if (log.length > 30) log.splice(0, log.length - 30);
}

/**
 * The editor shown lends its matches; the function returned takes them back (the editor going: another mode).
 */
export function setFindProvider(p: FindProvider): () => void {
    provider = p;
    // The editor of another mode takes over: where its search stands now (it may have searched before lending its matches).
    noteFindChange();
    return () => { if (provider === p) provider = null; };
}

/**
 * The bar's matches, or the one chosen, changed: the host is told where it stands at once, and sent the list a moment
 * later (`now`: at once) if it is open.
 */
export function noteFindChange(now = false) {
    if (!provider) return;
    const { total, index } = provider.state();
    transport.postMessage('FindState', { total, index });
    if (!open) return;
    if (now) { postResults(); return; }
    if (timer) clearTimeout(timer);
    timer = setTimeout(postResults, FIND_RESULTS_DELAY_MS);
}

transport.addListener<{ open: boolean }>('FindResultsOpen', arg => {
    open = !!arg?.open;
    if (timer) clearTimeout(timer);
    timer = null;
    // Opened: the list sent whatever was sent before (the host's list was emptied when it closed).
    sent.clear();
    lastTotal = -1;
    if (open) noteFindChange(true);
});

transport.addListener<{ index: number, version?: number }>('FindActivate', arg => {
    if (!provider || typeof arg?.index !== 'number') return;
    const tokens = arg.version != null ? sent.get(arg.version) : undefined;
    provider.activate(arg.index, tokens ? tokens[arg.index] ?? null : null);
});
