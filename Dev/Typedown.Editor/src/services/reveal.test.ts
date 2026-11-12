import { STANDARD_Y, leastToShow, toStandard, toStandardIfOut } from './reveal'

// The rule both editors bring things into view by (services/reveal).
describe('reveal', () => {
    it('puts a thing a third of the way down', () => {
        expect(toStandard(1000)).toBe(1000 - STANDARD_Y)
    })
    it('moves nothing in view, and what is out of it to the standard place', () => {
        expect(toStandardIfOut(500, 800)).toBe(0)
        expect(toStandardIfOut(-10, 800)).toBe(-10 - STANDARD_Y)
        expect(toStandardIfOut(900, 800)).toBe(900 - STANDARD_Y)
    })
    it('scrolls the least, keeping a margin', () => {
        expect(leastToShow(400, 20, 800)).toBe(0)
        expect(leastToShow(10, 20, 800)).toBe(10 - 48)
        expect(leastToShow(790, 20, 800)).toBe(790 + 20 - (800 - 48))
    })
})
