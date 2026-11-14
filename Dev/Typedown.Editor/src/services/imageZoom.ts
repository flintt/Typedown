// A picture's zoom (the image toolbar's 25% … 200%, written as `style="zoom:100%"` as Typora writes it) is of the
// screen's pixels: at 100% one pixel of the picture is one pixel of the screen. The page counts in CSS pixels, which
// Windows display scaling makes larger than the screen's: at 150% a screenshot at "100%" was drawn half as large again
// and blurred (the user, 2026-11-12). The file keeps what it says; only the editor draws it so.

const ZOOM = /(^|;)\s*zoom\s*:\s*([\d.]+)\s*(%?)\s*(?=;|$)/i

/** The style as drawn on a screen of that pixel ratio: its zoom divided by the ratio; anything else as it is. */
export function screenZoomStyle(style: string | undefined, pixelRatio: number): string | undefined {
    if (!style || !(pixelRatio > 0) || pixelRatio === 1) return style
    const m = ZOOM.exec(style)
    if (!m) return style
    const value = parseFloat(m[2]) / (m[3] ? 100 : 1)
    if (!(value > 0)) return style
    const drawn = Math.round(value / pixelRatio * 10000) / 10000
    return style.slice(0, m.index) + `${m[1]}zoom:${drawn}` + style.slice(m.index + m[0].length)
}

export const screenPixelRatio = () => (typeof window !== 'undefined' && window.devicePixelRatio) || 1
