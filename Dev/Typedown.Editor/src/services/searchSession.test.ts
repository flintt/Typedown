import { SearchSession } from './searchSession'

// Which search is the one asked for now (services/searchSession).
describe('SearchSession', () => {
    const regexp = { searchIsCaseSensitive: false, searchIsWholeWord: false, searchIsRegexp: true }
    it('is the query and its options, not the request', () => {
        const s = new SearchSession()
        s.ask('(a+)+$', regexp)
        s.ask('(a+)+$', { ...regexp })
        expect(s.isLatest('(a+)+$', regexp)).toBe(true)
    })
    it('takes the same text with other options for another search', () => {
        const s = new SearchSession()
        s.ask('a', regexp)
        s.ask('a', { ...regexp, searchIsWholeWord: true })
        expect(s.isLatest('a', regexp)).toBe(false)
        expect(s.isLatest('a', { ...regexp, searchIsWholeWord: true })).toBe(true)
    })
    it('has none once cleared', () => {
        const s = new SearchSession()
        s.ask('a', regexp)
        s.clear()
        expect(s.isLatest('a', regexp)).toBe(false)
    })
})
