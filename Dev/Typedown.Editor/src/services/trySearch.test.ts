export {}

// Each case with the module's state fresh (its worker, verdicts and queue).
let trySearch: typeof import('./common').trySearch
let searchMayRun: typeof import('./common').searchMayRun
let searchGivenUp: typeof import('./searchGivenUp').searchGivenUp

// An ES module jest does not take; search only (matchString), not the outline.
jest.mock('execall', () => () => [])

// The worker a regular expression is tried in, by hand: each job posted is kept, answered when the test says.
class FakeWorker {
    static all: FakeWorker[] = []
    posted: any[] = []
    terminated = false
    private listeners: ((e: MessageEvent) => void)[] = []
    constructor() { FakeWorker.all.push(this) }
    postMessage(data: any) { this.posted.push(data) }
    addEventListener(_: string, f: (e: MessageEvent) => void) { this.listeners.push(f) }
    removeEventListener(_: string, f: (e: MessageEvent) => void) { this.listeners = this.listeners.filter(l => l !== f) }
    terminate() { this.terminated = true }
    answer(job: any) { for (const l of [...this.listeners]) l({ data: job.id } as MessageEvent) }
}

const regexp = { searchIsRegexp: true, searchIsCaseSensitive: false, searchIsWholeWord: false }

beforeEach(() => {
    jest.useFakeTimers()
    FakeWorker.all = []
    ;(globalThis as any).Worker = FakeWorker
    ;(URL as any).createObjectURL = () => 'blob:worker'
    jest.isolateModules(() => {
        ({ trySearch, searchMayRun } = require('./common'))
        ;({ searchGivenUp } = require('./searchGivenUp'))
    })
})

afterEach(() => jest.useRealTimers())

// The audit of 2026-11-24, R03: with the worker busy on a slow expression, a quick one queued after it was given up on
// as slow too - its time was counted from when it was sent, not from when the worker took it.
describe('trySearch queue', () => {
    it('a quick expression queued behind a slow one has its own time, and comes through', () => {
        const texts = () => ['a'.repeat(40) + '!']
        const quickDone = jest.fn()
        trySearch('(x+)+$', regexp, texts, () => { })
        trySearch('a', regexp, texts, quickDone)
        jest.advanceTimersByTime(2000)
        // The slow one given up; its worker stopped.
        expect(FakeWorker.all[0].terminated).toBe(true)
        // The quick one now runs, on a worker of its own, and answers at once.
        const worker = FakeWorker.all[FakeWorker.all.length - 1]
        const job = worker.posted.find(p => p.source === 'a')
        expect(job).toBeDefined()
        worker.answer(job)
        expect(quickDone).toHaveBeenCalled()
        expect(searchMayRun('a', regexp)).toBe(true)
        expect(searchGivenUp()).toBe(false)
        expect(searchMayRun('(x+)+$', regexp)).toBe(false)
    })

    it('one job at a time: the second is sent when the first is answered', () => {
        const texts = () => ['bbb']
        trySearch('b', regexp, texts, () => { })
        trySearch('bb', regexp, texts, () => { })
        const worker = FakeWorker.all[0]
        expect(worker.posted.map(p => p.source)).toEqual(['b'])
        jest.advanceTimersByTime(1900)
        worker.answer(worker.posted[0])
        expect(worker.posted.map(p => p.source)).toEqual(['b', 'bb'])
        // Its own 2 s from now: 1.9 s more is not given up.
        jest.advanceTimersByTime(1900)
        worker.answer(worker.posted[1])
        expect(searchMayRun('bb', regexp)).toBe(true)
        expect(worker.terminated).toBe(false)
    })
})
