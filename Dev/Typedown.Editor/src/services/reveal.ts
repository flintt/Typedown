// Where a match, a heading or the caret is put when it is brought into view, and whether it needs to be: the rule both
// editors had, each its own copy (the modularity audit's M07). The scrolling is each editor's own - the source editor
// scrolls itself, the visual one the page - so these give how far to scroll, and the editor does it.

/** How far from the top of the window a thing brought into view is put: about a third of the way down. */
export const STANDARD_Y = 320

/** The distance to scroll for `y` (from the window's top) to come to the standard place. */
export const toStandard = (y: number) => y - STANDARD_Y

/** The same, only when `y` is out of the window (above it, or below `viewportHeight`); else 0. */
export const toStandardIfOut = (y: number, viewportHeight: number) => (y < 0 || y > viewportHeight ? y - STANDARD_Y : 0)

/**
 * The least scroll that brings a thing at `y`, `height` high, into the window with `margin` around it - an undo puts the
 * caret back without the page jumping; 0 when it is in view.
 */
export const leastToShow = (y: number, height: number, viewportHeight: number, margin = 48) =>
    y < margin ? y - margin : y + height > viewportHeight - margin ? y + height - (viewportHeight - margin) : 0
