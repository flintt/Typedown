import transport from "./transport"

const postScrollState = () => {
    transport.postMessage('OnScroll', {
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
        maximumX: document.body.scrollWidth - window.innerWidth,
        maximumY: document.body.scrollHeight - window.innerHeight,
        scrollX: window.scrollX,
        scrollY: window.scrollY
    })
}

let pendingFrame: number | undefined
const scheduleScrollState = () => {
    if (pendingFrame !== undefined) return
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
