export {};

const send = jest.fn();
let listeners: ((e: { data: string }) => void)[] = [];
let results: typeof import('./findResults');

beforeEach(() => {
    jest.resetModules();
    jest.useFakeTimers();
    send.mockReset();
    listeners = [];
    Object.defineProperty(window, 'chrome', {
        configurable: true,
        value: { webview: { postMessage: send, addEventListener: (_: string, l: any) => listeners.push(l) } }
    });
    results = require('./findResults');
});

afterEach(() => {
    jest.useRealTimers();
});

const deliver = (name: string, args: unknown) => listeners.forEach(l => l({ data: JSON.stringify({ name, args }) }));
// Each message as the host reads it: a diff put together with the one before of the same name (transport.ts).
const sent = (name: string) => {
    let state = '';
    const out: any[] = [];
    for (const [raw] of send.mock.calls) {
        const m = JSON.parse(raw);
        if (m.name !== name) continue;
        if (m.type === 'diffmsg') {
            state = m.diff ? state.slice(0, m.start) + m.args + state.slice(m.end) : m.args;
            out.push(JSON.parse(state));
        } else out.push(m.args);
    }
    return out;
};

function provider(total: number, index = 0) {
    const activated: [number, unknown][] = [];
    // The same object for the same match, as the editors' own matches are.
    const tokens: { match: number }[] = [];
    const p = {
        total, index,
        begin: jest.fn(),
        state: () => ({ total: p.total, index: p.index }),
        preview: (i: number) => ({ text: `match ${i}`, start: 6, end: 6 + String(i).length }),
        token: (i: number) => (tokens[i] ??= { match: i }),
        activate: (i: number, token: unknown) => { activated.push([i, token]); },
        activated,
    };
    return p;
}

describe('findResults', () => {
    it('tells the host the count and the match chosen, the list only while it is open', () => {
        const p = provider(3, 1);
        // Lent: where it stands is told at once.
        results.setFindProvider(p);
        expect(sent('FindState')).toEqual([{ total: 3, index: 1 }]);
        jest.runAllTimers();
        expect(sent('FindResults')).toEqual([]);

        deliver('FindResultsOpen', { open: true });
        // At once on opening.
        const [list] = sent('FindResults');
        expect(list).toMatchObject({ version: 1, total: 3, index: 1, capped: false });
        expect(list.entries).toEqual([0, 1, 2].map(i => ({ text: `match ${i}`, start: 6, end: 7 })));
        expect(p.begin).toHaveBeenCalledTimes(1);

        // Only the match chosen moved (a step, an entry chosen): the host is told where the bar is, the list not sent again.
        p.index = 2;
        results.noteFindChange();
        jest.advanceTimersByTime(results.FIND_RESULTS_DELAY_MS);
        expect(sent('FindResults')).toHaveLength(1);
        expect(sent('FindState').pop()).toEqual({ total: 3, index: 2 });

        // New matches: sent again, a moment after the last change.
        p.total = 4;
        results.noteFindChange();
        results.noteFindChange();
        expect(sent('FindResults')).toHaveLength(1);
        jest.advanceTimersByTime(results.FIND_RESULTS_DELAY_MS);
        expect(sent('FindResults')).toHaveLength(2);
        expect(sent('FindResults')[1]).toMatchObject({ version: 2, total: 4, index: 2 });
    });

    it('caps the entries and keeps the count exact', () => {
        results.setFindProvider(provider(1234));
        deliver('FindResultsOpen', { open: true });
        const [list] = sent('FindResults');
        expect(list.total).toBe(1234);
        expect(list.capped).toBe(true);
        expect(list.entries).toHaveLength(results.FIND_RESULTS_CAP);
    });

    it('passes an entry chosen back with the token of the list it was chosen in', () => {
        const p = provider(5);
        results.setFindProvider(p);
        deliver('FindResultsOpen', { open: true });
        deliver('FindActivate', { index: 3, version: 1 });
        expect(p.activated).toEqual([[3, { match: 3 }]]);
        // A list nobody sent: the index alone.
        deliver('FindActivate', { index: 2, version: 99 });
        expect(p.activated[1]).toEqual([2, null]);
    });

    it('sends nothing once closed, and nothing without an editor', () => {
        const p = provider(2);
        const off = results.setFindProvider(p);
        deliver('FindResultsOpen', { open: true });
        deliver('FindResultsOpen', { open: false });
        results.noteFindChange();
        jest.runAllTimers();
        expect(sent('FindResults')).toHaveLength(1);
        off();
        send.mockReset();
        results.noteFindChange();
        expect(send).not.toHaveBeenCalled();
    });
});
