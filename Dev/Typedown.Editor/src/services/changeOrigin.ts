import transport from "services/transport";

// Where a change to the document text came from, as the page reports it with MarkdownChange:
//
//   user   - the reader did something that edits: a trusted key, input, paste, cut, drop or pointer event in the
//            page, or an editing command the host forwarded from its menus or shortcuts.
//   editor - the text changed with no such action before it: a re-render, a late asynchronous render, a
//            normalization. The host must not take it as the reader's edit (docs/automation-api-spec.md).
//
// The page cannot follow one action to the change it causes (Muya reports changes throttled, CodeMirror at once),
// so an action arms a flag and the next change report consumes it. A load clears it: the load's text is the
// host's, and so is whatever the editor reports for it.

export type ChangeOrigin = 'user' | 'editor';

/** Host messages that edit the document on the reader's behalf. */
const editingCommands = ['Cut', 'Paste', 'DeleteSelection', 'DeleteParagraph', 'Duplicate', 'Format', 'InsertImage',
    'InsertParagraph', 'InsertTable', 'Replace', 'UpdateParagraph', 'ImportFile'];
const inputEvents = ['keydown', 'beforeinput', 'paste', 'cut', 'drop', 'compositionend', 'pointerdown'];

let armed = false;

const beforeIntent = new Set<() => void>();

/**
 * Called before the reader's action edits (and before it is armed): the page finishes a load still being reported
 * there, so the edit is never taken for the load's own text (components/Editor/index.tsx).
 */
export function onUserIntent(callback: () => void): () => void {
    beforeIntent.add(callback);
    return () => { beforeIntent.delete(callback) };
}

/** Records that the reader did something that may edit the document. */
export function noteUserIntent() {
    for (const callback of beforeIntent) {
        try { callback() } catch (e) { console.log(e) }
    }
    armed = true;
}

/** Forgets any pending action: the host is about to replace the content. */
export function resetUserIntent() {
    armed = false;
    editKind = null;
    caretMoved = false;
    insertedText = '';
}

/** The origin of the change being reported now; consumes the pending action. */
export function takeChangeOrigin(): ChangeOrigin {
    const origin = armed ? 'user' : 'editor';
    armed = false;
    return origin;
}

// What kind of edit the reader made since the last change report, for the host's undo steps (ContentHistory): the host
// gets whole-document snapshots and could tell steps apart only by a pause or a caret on another line, so a selection
// replaced, a format, a paste, a word deleted, a caret moved within the paragraph all merged into the typing before
// them (the user, 2026-11-05, #39). The kinds: "insert" (letters typed), "delete" (Backspace or Delete, a character at a
// time), "other" (anything else that edits: Enter, a paste, a cut, a format, a word deleted, a drag, a command of the
// host's). Several in one report (Muya's reports are throttled) are "other" unless all alike. "moved": the caret was
// put somewhere else first (an arrow, Home, End, a click, a shortcut). "inserted": the letters typed, for where a
// typing step ends (a sentence).
export type EditKind = 'insert' | 'delete' | 'other';

export interface EditReport {
    kind: EditKind | null;
    moved: boolean;
    inserted: string;
}

let editKind: EditKind | null = null;
let caretMoved = false;
let insertedText = '';

function noteEdit(kind: EditKind) {
    editKind = editKind == null || editKind === kind ? kind : 'other';
}

const navigationKeys = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown']);

function onKeyDown(e: KeyboardEvent) {
    if (!e.isTrusted || e.isComposing) return;
    if (navigationKeys.has(e.key)) caretMoved = true;
    else if (e.key === 'Backspace' || e.key === 'Delete') noteEdit(e.ctrlKey || e.altKey || e.metaKey ? 'other' : 'delete');
    else if (e.key === 'Enter' || e.key === 'Tab') noteEdit('other');
    // A shortcut: a step begins after it (Ctrl+A, Ctrl+C); one that edits reports its own kind (the host's command).
    else if ((e.ctrlKey || e.metaKey) && e.key.length === 1) caretMoved = true;
}

function onBeforeInput(e: InputEvent) {
    if (!e.isTrusted) return;
    const type = e.inputType || '';
    if (type.startsWith('history')) return;
    if (type === 'insertText' || type === 'insertCompositionText' || type === 'insertReplacementText') {
        noteEdit('insert');
        insertedText += e.data || '';
    } else if (type === 'deleteContentBackward' || type === 'deleteContentForward') noteEdit('delete');
    else noteEdit('other');
}

/** The kind of edit since the last report; consumes it. Taken before takeChangeOrigin (it reads the armed flag). */
export function takeEditReport(): EditReport {
    const report = { kind: editKind ?? (armed ? 'other' as EditKind : null), moved: caretMoved, inserted: insertedText.slice(-200) };
    editKind = null;
    caretMoved = false;
    insertedText = '';
    return report;
}

let installed = false;

export function installChangeOriginTracking() {
    if (installed) return;
    installed = true;
    for (const type of inputEvents)
        window.addEventListener(type, e => { if (e.isTrusted) noteUserIntent() }, true);
    window.addEventListener('keydown', onKeyDown, true);
    window.addEventListener('beforeinput', onBeforeInput as EventListener, true);
    window.addEventListener('pointerdown', e => { if (e.isTrusted) caretMoved = true }, true);
    for (const type of ['paste', 'cut', 'drop']) window.addEventListener(type, e => { if (e.isTrusted) noteEdit('other') }, true);
    // Registered at start-up, before the components' own listeners, so the command counts before it edits.
    for (const name of editingCommands) transport.addListener(name, () => { noteUserIntent(); noteEdit('other') });
    for (const name of ['LoadFile', 'SetMarkdown']) transport.addListener(name, resetUserIntent);
}
