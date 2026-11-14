import { screenZoomStyle } from './imageZoom'

test('a zoom is of the screen\'s pixels: at 150% display scaling, 100% is drawn at 1/1.5', () => {
    expect(screenZoomStyle('zoom:100%;', 1.5)).toBe('zoom:0.6667;')
    expect(screenZoomStyle('zoom: 50%', 1.5)).toBe('zoom:0.3333')
    expect(screenZoomStyle('zoom:2', 2)).toBe('zoom:1')
})

test('the rest of the style stays, and nothing changes without a zoom or at a ratio of 1', () => {
    expect(screenZoomStyle('width:300px;zoom:100%;border:0', 1.25)).toBe('width:300px;zoom:0.8;border:0')
    expect(screenZoomStyle('width:300px', 1.5)).toBe('width:300px')
    expect(screenZoomStyle('zoom:100%', 1)).toBe('zoom:100%')
    expect(screenZoomStyle(undefined, 1.5)).toBeUndefined()
    expect(screenZoomStyle('zoom:abc', 1.5)).toBe('zoom:abc')
})
