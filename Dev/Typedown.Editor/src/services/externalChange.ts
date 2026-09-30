// Showing the reader what a connected program just changed (assets/styles/index.css, .td-external-change).

export const EXTERNAL_CHANGE_CLASS = 'td-external-change';
/** As long as the fade in the stylesheet. */
export const EXTERNAL_CHANGE_MS = 2000;

/** Highlights these elements briefly; elements re-rendered meanwhile simply lose it. */
export function highlightExternalChange(elements: (Element | null)[]) {
    const shown = elements.filter((e): e is Element => !!e);
    for (const element of shown) {
        // Restart the animation when the same block changes again before it ended.
        element.classList.remove(EXTERNAL_CHANGE_CLASS);
        void (element as HTMLElement).offsetWidth;
        element.classList.add(EXTERNAL_CHANGE_CLASS);
    }
    setTimeout(() => shown.forEach(e => e.classList.remove(EXTERNAL_CHANGE_CLASS)), EXTERNAL_CHANGE_MS);
}
