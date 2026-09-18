export {};

const send = jest.fn();
let transport: typeof import('./transport').default;

beforeEach(() => {
    jest.resetModules();
    send.mockReset();
    Object.defineProperty(window, 'chrome', {
        configurable: true,
        value: { webview: { postMessage: send, addEventListener: jest.fn() } }
    });
    transport = require('./transport').default;
});

const messages = () => send.mock.calls.map(([message]) => JSON.parse(message));

function reconstruct() {
    let state = '';
    for (const message of messages()) {
        state = message.diff
            ? state.slice(0, message.start) + message.args + state.slice(message.end)
            : message.args;
    }
    return JSON.parse(state);
}

test.each(['OnScroll', 'SelectionFormats'])('deduplicates unchanged %s state', name => {
    transport.postMessage(name, { value: 1 });
    for (let i = 0; i < 100; i++) transport.postMessage(name, { value: 1 });
    expect(send).toHaveBeenCalledTimes(1);
    transport.postMessage(name, { value: 2 });
    expect(send).toHaveBeenCalledTimes(2);
    expect(reconstruct()).toEqual({ value: 2 });
});

test.each(['OpenURI', 'FileLoaded'])('preserves repeated %s events', name => {
    transport.postMessage(name, { text: 'same' });
    transport.postMessage(name, { text: 'same' });
    expect(send).toHaveBeenCalledTimes(2);
    expect(reconstruct()).toEqual({ text: 'same' });
});

test('reconstructs insertions, deletions and Unicode changes', () => {
    for (const text of ['hello', 'hello 世界 😀', '世界', '', 'new document']) {
        transport.postMessage('MarkdownChange', { text });
        expect(reconstruct()).toEqual({ text });
    }
});

test('does not advance the diff baseline after a failed send', () => {
    transport.postMessage('MarkdownChange', { text: 'initial' });
    send.mockImplementationOnce(() => { throw new Error('send failed'); });
    expect(() => transport.postMessage('MarkdownChange', { text: 'lost' })).toThrow('send failed');
    // The failed call was never delivered to the host.
    send.mock.calls.pop();
    transport.postMessage('MarkdownChange', { text: 'next' });
    expect(reconstruct()).toEqual({ text: 'next' });
});
