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
