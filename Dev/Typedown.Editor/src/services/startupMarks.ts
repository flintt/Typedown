import transport from './transport';

// The page's moments of the start, each once, as wall-clock ms (performance.timeOrigin + now): sent to the host with
// the first document drawn, which writes them on its start-up time line ("startup: page ..." in debug.log). With the
// navigation's own timing (the HTML received, the DOM ready) and the main script's download: where a start's time goes
// inside the page - the script's download, its parsing and running, React, Muya, the settings, the document.
const marks: Record<string, number> = {};
let sent = false;

const wall = (ms: number) => Math.round(performance.timeOrigin + ms);

export function markStartup(name: string) {
    if (sent || name in marks) return;
    marks[name] = wall(performance.now());
}

export function sendStartupMarks() {
    if (sent) return;
    sent = true;
    try {
        const all: Record<string, number> = { navigation: wall(0) };
        const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming | undefined;
        if (nav) {
            all['html received'] = wall(nav.responseEnd);
            all['dom interactive'] = wall(nav.domInteractive);
        }
        // The main script: its download done (resource timing), the rest is its parsing and running.
        const script = performance.getEntriesByType('resource')
            .find(e => /\/static\/js\/main\.[^/]*\.js$/.test(e.name)) as PerformanceResourceTiming | undefined;
        if (script) all['script downloaded'] = wall(script.responseEnd);
        transport.postMessage('StartupMarks', { ...all, ...marks });
    } catch {
        // timing only: never in the way
    }
}
