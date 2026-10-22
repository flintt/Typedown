import { readScrollAnchor } from './loadPreview'
import transport from "./transport"

// The load the page is showing. A report without one comes from a page that has not been given a document
// yet — a freshly navigated page reports its empty body's first layout as "scrolled to 0", and the host
// took that for the reader's position and handed it back on the very next load: leaving the settings, which
// navigates the page again, always came back at the top.
let loadId: number | undefined
export const setScrollLoadId = (id: number | undefined) => { loadId = id }
/** The load the page is showing (or about to). */
export const currentLoadId = () => loadId

const postScrollState = () => {
    transport.postMessage('OnScroll', {
        loadId,
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
        maximumX: document.body.scrollWidth - window.innerWidth,
        maximumY: document.body.scrollHeight - window.innerHeight,
        scrollX: window.scrollX,
        scrollY: window.scrollY,
        // The block at the top of the window, kept with the offset: what the next load puts back (services/loadPreview).
        anchor: readScrollAnchor()
    })
}

let pendingFrame: number | undefined
let pendingTimer: number | undefined
// While a long document is drawn a part at a time the page grows (and is kept in place) every frame: a report each time
// found the block at the top of the window and laid the page out for it - seconds over a 1.5 MB document. Then at most
// one every quarter of a second; the last one comes when the drawing ends (the page's height changes with it).
const DRAWING_REPORT_MS = 250
const scheduleScrollState = () => {
    if (pendingFrame !== undefined || pendingTimer !== undefined) return
    if ((window as any).__typedownMuya?.contentState?.stateRender?.progressive) {
        pendingTimer = window.setTimeout(() => {
            pendingTimer = undefined
            postScrollState()
        }, DRAWING_REPORT_MS)
        return
    }
    pendingFrame = requestAnimationFrame(() => {
        pendingFrame = undefined
        postScrollState()
    })
}

const resizeObserver = new ResizeObserver(scheduleScrollState)
resizeObserver.observe(document.body)
addEventListener('scroll', scheduleScrollState, { passive: true })
addEventListener('resize', scheduleScrollState)

transport.addListener<{ scrollX: number, scrollY: number }>('OnScroll', ({ scrollX, scrollY }) => {
    const equals = (a: number, b: number) => Math.abs(a - b) < 1
    if (!equals(scrollX, window.scrollX) || !equals(scrollY, window.scrollY))
        window.scrollTo(scrollX, scrollY)
})
