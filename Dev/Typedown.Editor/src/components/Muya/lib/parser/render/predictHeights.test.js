import { stack, collapseList } from './predictHeights'

// How predicted boxes stack (predictHeights.js): as CSS stacks blocks.
describe('stack', () => {
  const block = (h, mt = 0, mb = 0) => ({ h, mt, mb, inline: false, through: false })

  it('collapses the margin between two blocks to the larger', () => {
    const s = stack([block(50, 8, 8), block(30, 16, 16)])
    expect(s.tops).toEqual([8, 74])
    expect(s.end).toBe(104)
    expect(s.trail).toEqual([16])
  })

  it('collapses a negative margin with a positive one', () => {
    expect(collapseList([16, -4])).toBe(12)
    expect(collapseList([-4, -10])).toBe(-10)
    expect(collapseList([])).toBe(0)
  })

  it('collapses margins through an empty block', () => {
    const empty = { h: 0, mt: 20, mb: 20, inline: false, through: true }
    const s = stack([block(10, 0, 8), empty, block(10, 8, 0)])
    // 8, 20, 20 and 8 meet in one margin of 20.
    expect(s.tops[2]).toBe(30)
    expect(s.end).toBe(40)
  })

  it('keeps an inline-level block (a code block) on a line of its own: its margins collapse with nothing', () => {
    const code = { h: 100, mt: 7, mb: 7, inline: true, through: false }
    const s = stack([block(10, 8, 8), code, block(10, 8, 8)])
    // The line starts after the 8 below the first block; the next block's 8 is not shared with the code block's 7.
    expect(s.tops).toEqual([8, 33, 148])
    expect(s.end).toBe(158)
  })

  it('reports the margins before the first box, which may go through the parent', () => {
    const s = stack([block(10, 12, 0)])
    expect(s.lead).toEqual([12])
    expect(s.firstTop).toBe(12)
    expect(s.any).toBe(true)
  })

  it('starts from margins already pending (the last drawn block above the rest)', () => {
    const s = stack([block(10, 8, 8)], [16])
    expect(s.tops).toEqual([16])
  })
})
