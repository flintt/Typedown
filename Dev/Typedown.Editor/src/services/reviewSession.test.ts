jest.mock('./transport', () => ({ __esModule: true, default: { postMessage: () => undefined, addListener: () => () => undefined } }))
import { revisionTime } from './reviewSession'

test('a revision\'s time: today hours and minutes, this year with the day, older with the year', () => {
    const now = new Date(2026, 10, 6, 15, 30)
    expect(revisionTime(new Date(2026, 10, 6, 9, 5).getTime(), 'en-GB', now)).toBe('09:05')
    const thisYear = revisionTime(new Date(2026, 2, 4, 9, 5).getTime(), 'en-GB', now)
    expect(thisYear).toMatch(/0?4\/0?3.*09:05/)
    expect(thisYear).not.toMatch(/2026/)
    expect(revisionTime(new Date(2025, 2, 4, 9, 5).getTime(), 'en-GB', now)).toMatch(/2025/)
    expect(revisionTime(new Date(2026, 10, 6, 9, 5).getTime(), 'zh-CN', now)).toBe('09:05')
})
