jest.mock('services/remote/common', () => ({ __esModule: true, default: { openNewWindow: jest.fn() } }))

// eslint-disable-next-line import/first
import { linkText } from './linkActions'

// What Remove link leaves of each form of link (services/linkActions; the menu knew only the first).
describe('linkText', () => {
    it('keeps an inline link\'s text', () => {
        expect(linkText('[the site](https://example.com "title")')).toBe('the site')
        expect(linkText('[**bold** text](a.md)')).toBe('**bold** text')
    })
    it('keeps a reference link\'s text', () => {
        expect(linkText('[text][ref]')).toBe('text')
        expect(linkText('[text][]')).toBe('text')
        expect(linkText('[text]')).toBe('text')
    })
    it('keeps an autolink\'s address', () => {
        expect(linkText('<https://example.com/a>')).toBe('https://example.com/a')
        expect(linkText('<me@example.com>')).toBe('me@example.com')
    })
    it('keeps an HTML link\'s text', () => {
        expect(linkText('<a href="x.md">x</a>')).toBe('x')
    })
    it('leaves what is no link of these forms', () => {
        expect(linkText('https://example.com')).toBeNull()
        expect(linkText('')).toBeNull()
        expect(linkText('<b>bold</b>')).toBeNull()
    })
})

// The menu's Edit link: the address chosen in the link's Markdown, to type over.
describe('editLink', () => {
    const { editLink } = require('./linkActions')
    const state = (text: string) => {
        const block = { key: 'b1', text }
        return { block, cursor: null as any, getBlock: (key: string) => key === 'b1' ? block : null, render: jest.fn() }
    }
    it('chooses an inline link\'s address', () => {
        const s = state('See [the site](https://example.com "t") now')
        const raw = '[the site](https://example.com "t")'
        expect(editLink(s, { key: 'b1', start: 4, end: 4 + raw.length, raw })).toBe(true)
        expect(s.block.text.slice(s.cursor.start.offset, s.cursor.end.offset)).toBe('https://example.com')
        expect(s.render).toHaveBeenCalled()
    })
    it('chooses a link of another form whole, and nothing when the text has moved on', () => {
        const s = state('x <https://a.org> y')
        expect(editLink(s, { key: 'b1', start: 2, end: 17, raw: '<https://a.org>' })).toBe(true)
        expect(s.block.text.slice(s.cursor.start.offset, s.cursor.end.offset)).toBe('<https://a.org>')
        expect(editLink(s, { key: 'b1', start: 0, end: 5, raw: '[gone](x)' })).toBe(false)
    })
})
