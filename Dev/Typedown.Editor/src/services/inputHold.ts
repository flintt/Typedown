// What the reader types while a tab's document is on its way to the page (docs/todo.md, "标签加载期间按键丢失").
//
// The host changes the tab at once and the page shows the new document a moment later. Keys typed in between went into
// the document the page still showed and were dropped with it. Now the host says "hold" first, before anything that
// waits: the page moves the keyboard to a hidden text box of its own, outside the editor, where letters, Enter and an
// input method's text collect as in any text box. Each load names the newest hold the host had asked for; once the
// page has that document (its FileLoaded sent), what was typed for it goes in at the caret as the reader's own edit and
// the editor takes the keyboard back.
//
// Several holds in a row (tabs switched quickly): what was typed after one hold and before the next belongs to the
// document that hold was for, and goes into it when its load is in; the last hold's text goes to the last document.
// A hold the host gives up (a switch that was not needed after all) is released: its text goes to the document shown.

export interface InputHoldEnv {
    /** False where keys are not text: reading mode, Vim in source mode. */
    canHold(): boolean;
    /** Puts typed text in at the editor's caret as the reader's edit; resolves once the editor holds it. */
    insert(text: string): Promise<void>;
    /** Gives the keyboard back to the editor. */
    focusEditor(): void;
    /** Nothing is on its way into the editor (no load being shown or confirmed): text can go in now. */
    idle(): boolean;
}

interface Segment { hold: number; text: string }

// The last few things the hold did, for a test that fails to say what happened (read with test.editor.eval).
const trace: string[] = [];
const note = (what: string) => {
    trace.push(`${Math.round(performance.now())} ${what}`);
    if (trace.length > 120) trace.shift();
};
(window as any).__inputHoldTrace = trace;
const where = (e: EventTarget | null) => {
    const el = e as HTMLElement | null;
    return el ? `${el.tagName?.toLowerCase()}${el.id ? '#' + el.id : ''}${el.hasAttribute?.('data-input-hold') ? '[hold]' : ''}${el.className && typeof el.className === 'string' ? '.' + el.className.split(' ')[0] : ''}` : 'none';
};
// Where each key and each focus change went, for the same purpose.
document.addEventListener('keydown', e => note(`key ${e.key} -> ${where(document.activeElement)}`), true);
document.addEventListener('focusin', e => note(`focus in ${where(e.target)}`), true);
window.addEventListener('blur', () => note('window blur'));
window.addEventListener('focus', () => note('window focus'));

export class InputHold {
    /** The newest hold the host asked for. */
    latest = 0;
    /** The text box has the keyboard. */
    active = false;
    /** Text typed for earlier holds, waiting for their documents. */
    segments: Segment[] = [];
    private sink: HTMLTextAreaElement | null = null;
    private composing = false;
    private startWhenComposed = false;
    private safety: number | undefined;
    private delivering: Promise<void> = Promise.resolve();
    private readonly onFocusIn = (e: FocusEvent) => {
        // The editor taking the focus for itself as its new document comes in (a caret placed): the keys stay here.
        if (this.active && this.sink && e.target !== this.sink) this.sink.focus({ preventScroll: true });
    };

    constructor(private readonly env: InputHoldEnv, private readonly safetyMs = 5000) {
        document.addEventListener('compositionstart', () => { this.composing = true }, true);
        document.addEventListener('compositionend', () => {
            this.composing = false;
            if (this.startWhenComposed) { this.startWhenComposed = false; this.start() }
        }, true);
    }

    /** The host is about to show another document (HoldInput). */
    hold(id: number) {
        note(`hold ${id} (latest ${this.latest}, active ${this.active}, composing ${this.composing}, can ${this.env.canHold()})`);
        if (id <= this.latest) return;
        // A test showing what is lost without the hold turns it off (test.editor.eval).
        if ((window as any).__inputHoldDisabled) { this.latest = id; return }
        if (this.active) this.freeze();
        this.latest = id;
        if (!this.env.canHold()) return;
        this.armSafety();
        if (this.active) return;
        // An input method composing in the editor is not interrupted: its text goes where it was being typed.
        if (this.composing) { this.startWhenComposed = true; return }
        this.start();
    }

    /** Whether text is waiting for the document of this hold (its load must not be skipped for a newer one). */
    hasTextFor(hold: number | undefined) {
        return typeof hold === 'number' && this.segments.some(s => s.hold === hold && s.text.length > 0);
    }

    /** The document of this hold is in the editor and confirmed to the host. */
    loaded(hold: number | undefined) {
        note(`loaded ${hold} (latest ${this.latest}, active ${this.active}, box ${JSON.stringify(this.sink?.value ?? '')}, waiting ${this.segments.length})`);
        if (typeof hold !== 'number') return;
        // Earlier holds whose documents never came (skipped, or superseded) give their text to this one rather than lose it.
        const text = this.take(s => s.hold <= hold);
        const last = hold >= this.latest && (this.active || this.startWhenComposed);
        const tail = last ? this.end() : '';
        this.deliver(text + tail, last);
    }

    /** The host will send no document for this hold: what was typed for it goes to the document shown. */
    release(hold: number, loadPending: boolean) {
        note(`release ${hold} (load pending ${loadPending})`);
        if (loadPending) return;
        this.loaded(hold);
    }

    private start() {
        if (!this.sink) {
            const sink = document.createElement('textarea');
            sink.setAttribute('aria-hidden', 'true');
            sink.setAttribute('data-input-hold', '');
            sink.tabIndex = -1;
            Object.assign(sink.style, {
                position: 'fixed', width: '2px', height: '1.5em', opacity: '0', border: '0', padding: '0',
                resize: 'none', overflow: 'hidden', pointerEvents: 'none', zIndex: '-1', caretColor: 'transparent',
            });
            // Tab is not text and would move the keyboard on; Escape and the rest of the page's keys are not wanted here.
            sink.addEventListener('keydown', e => { if (e.key === 'Tab') e.preventDefault() });
            document.body.appendChild(sink);
            this.sink = sink;
        }
        // Where the caret was, so an input method's candidate window opens there.
        const rect = caretRect();
        this.sink.style.left = `${Math.max(0, rect?.left ?? 0)}px`;
        this.sink.style.top = `${Math.max(0, rect?.top ?? 0)}px`;
        this.sink.value = '';
        this.active = true;
        document.addEventListener('focusin', this.onFocusIn, true);
        this.sink.focus({ preventScroll: true });
    }

    /** What was typed so far belongs to the current hold; the text box starts empty for the next. */
    private freeze() {
        const text = this.sink?.value ?? '';
        if (text) this.segments.push({ hold: this.latest, text });
        if (this.sink) this.sink.value = '';
    }

    private end() {
        this.startWhenComposed = false;
        if (!this.active) return '';
        const text = this.sink?.value ?? '';
        this.active = false;
        document.removeEventListener('focusin', this.onFocusIn, true);
        if (this.sink) { this.sink.value = ''; this.sink.blur() }
        clearTimeout(this.safety);
        return text;
    }

    private take(which: (s: Segment) => boolean) {
        const taken = this.segments.filter(which);
        this.segments = this.segments.filter(s => !which(s));
        return taken.map(s => s.text).join('');
    }

    private deliver(text: string, focus: boolean) {
        if (!text && !focus) return;
        // One after another, in the order typed: a later text never overtakes an earlier one still going in.
        this.delivering = this.delivering.then(async () => {
            try {
                note(`insert ${JSON.stringify(text)} focus ${focus}`);
                if (text) await this.env.insert(text);
            } catch (e) {
                console.log('typed text could not go in', e);
            }
            if (focus && !this.active) this.env.focusEditor();
        });
    }

    /** Text typed for an earlier document is still waiting for it. */
    get waiting() {
        return this.segments.length > 0;
    }

    /**
     * Resolves once nothing typed for an earlier document is waiting and what went in is in the editor - what the host's
     * flush before the next tab change waits for, so the document it keeps has the text. Gives up after a while.
     */
    async settled(timeoutMs = 4000) {
        const until = Date.now() + timeoutMs;
        while (this.waiting && Date.now() < until) await new Promise(r => setTimeout(r, 20));
        await this.delivering;
    }

    // A hold that nothing ends (no document and no release came): after a while the text goes to the document shown.
    private armSafety() {
        clearTimeout(this.safety);
        this.safety = window.setTimeout(() => {
            if (!this.active && !this.segments.length) return;
            // A long document still being shown: wait for it, its load ends the hold.
            if (!this.env.idle()) { this.armSafety(); return }
            this.loaded(this.latest);
        }, this.safetyMs);
    }
}

function caretRect(): DOMRect | null {
    try {
        const selection = document.getSelection();
        if (!selection || selection.rangeCount === 0) return null;
        const rect = selection.getRangeAt(0).getBoundingClientRect();
        return rect.width || rect.height || rect.left || rect.top ? rect : null;
    } catch {
        return null;
    }
}
