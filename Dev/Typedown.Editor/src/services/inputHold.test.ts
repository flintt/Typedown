import { InputHold } from "./inputHold";

// The hidden text box as the reader's keys fill it.
const type = (text: string) => {
    const sink = document.querySelector<HTMLTextAreaElement>('textarea[data-input-hold]')!;
    expect(document.activeElement).toBe(sink);
    sink.value += text;
};

const setup = (opts: { canHold?: boolean, idle?: boolean } = {}) => {
    const inserted: string[] = [];
    const focused: number[] = [];
    let idle = opts.idle ?? true;
    const hold = new InputHold({
        canHold: () => opts.canHold ?? true,
        insert: async text => { inserted.push(text) },
        focusEditor: () => { focused.push(inserted.length) },
        idle: () => idle,
    }, 1000);
    return { hold, inserted, focused, setIdle: (v: boolean) => { idle = v } };
};

const settle = () => new Promise(r => setTimeout(r, 0));

afterEach(() => document.querySelectorAll('textarea[data-input-hold]').forEach(e => e.remove()));

test('what is typed after a hold goes into the document of that hold, then the editor has the keyboard', async () => {
    const { hold, inserted, focused } = setup();
    hold.hold(1);
    type('xy\n中文');
    hold.loaded(1);
    await hold.settled();
    expect(inserted).toEqual(['xy\n中文']);
    expect(focused).toEqual([1]);
    expect(hold.active).toBe(false);
});

test('nothing typed: nothing goes in, the keyboard still goes back', async () => {
    const { hold, inserted, focused } = setup();
    hold.hold(1);
    hold.loaded(1);
    await hold.settled();
    expect(inserted).toEqual([]);
    expect(focused).toEqual([0]);
});

test('tabs switched quickly: each text goes to the document it was typed for, the keys stay held until the last', async () => {
    const { hold, inserted, focused } = setup();
    hold.hold(1);
    type('abc');
    hold.hold(2);
    type('def');
    expect(hold.hasTextFor(1)).toBe(true);
    hold.loaded(1);
    await hold.settled();
    expect(inserted).toEqual(['abc']);
    expect(focused).toEqual([]);
    expect(hold.active).toBe(true);
    type('g');
    hold.loaded(2);
    await hold.settled();
    expect(inserted).toEqual(['abc', 'defg']);
    expect(focused).toEqual([2]);
});

test('a document skipped on the way gives its text to the next one rather than lose it', async () => {
    const { hold, inserted } = setup();
    hold.hold(1);
    type('abc');
    hold.hold(2);
    type('def');
    hold.loaded(2);
    await hold.settled();
    expect(inserted).toEqual(['abcdef']);
});

test('a load of an older hold (a reload of the document shown) does not end a newer hold', async () => {
    const { hold, inserted, focused } = setup();
    hold.hold(3);
    type('k');
    hold.loaded(2);
    await hold.settled();
    expect(inserted).toEqual([]);
    expect(hold.active).toBe(true);
    hold.loaded(3);
    await hold.settled();
    expect(inserted).toEqual(['k']);
    expect(focused).toEqual([1]);
});

test('released without a document coming: the text goes to the document shown; with one coming, it waits for it', async () => {
    const { hold, inserted } = setup();
    hold.hold(1);
    type('a');
    hold.release(1, true);
    await settle();
    expect(inserted).toEqual([]);
    hold.release(1, false);
    await hold.settled();
    expect(inserted).toEqual(['a']);
});

test('where keys are not text (reading mode, Vim) nothing is held', async () => {
    const { hold, inserted, focused } = setup({ canHold: false });
    hold.hold(1);
    expect(hold.active).toBe(false);
    expect(document.querySelector('textarea[data-input-hold]')).toBeNull();
    hold.loaded(1);
    await hold.settled();
    expect(inserted).toEqual([]);
    expect(focused).toEqual([]);
});

test('an input method composing when the hold comes is not interrupted: the hold starts once it is done', async () => {
    const { hold } = setup();
    document.dispatchEvent(new CompositionEvent('compositionstart'));
    hold.hold(1);
    expect(hold.active).toBe(false);
    document.dispatchEvent(new CompositionEvent('compositionend'));
    expect(hold.active).toBe(true);
    type('x');
});

test('the editor taking the focus while held gives it back to the text box', () => {
    const { hold } = setup();
    const editor = document.createElement('div');
    editor.tabIndex = 0;
    document.body.appendChild(editor);
    hold.hold(1);
    editor.focus();
    type('x');
    editor.remove();
});

test('nothing ends the hold: after a while the text goes to the document shown, not while one is still coming', async () => {
    jest.useFakeTimers();
    try {
        const { hold, inserted, setIdle } = setup({ idle: false });
        hold.hold(1);
        type('late');
        jest.advanceTimersByTime(1100);
        expect(hold.active).toBe(true);
        setIdle(true);
        jest.advanceTimersByTime(1100);
        expect(hold.active).toBe(false);
        jest.useRealTimers();
        await hold.settled();
        expect(inserted).toEqual(['late']);
    } finally {
        jest.useRealTimers();
    }
});

test('settled waits for text still waiting for its document', async () => {
    const { hold, inserted } = setup();
    hold.hold(1);
    type('abc');
    hold.hold(2);
    let done = false;
    const waiting = hold.settled().then(() => { done = true });
    await new Promise(r => setTimeout(r, 50));
    expect(done).toBe(false);
    hold.loaded(1);
    await waiting;
    expect(inserted).toEqual(['abc']);
});
