// Which search is the one asked for now, for a search done again once its regular expression came through the worker
// (services/common trySearch): the query and its options together, the same rule in both editors. The visual editor
// compared the text alone - "a" whole word and "a" anywhere were one search, and the one no longer asked for was done
// again; the source editor compared the request object - the same search asked twice (the bar's message, then the
// state) dropped the first's callback, the only one trySearch keeps while the expression is tried, and its matches never
// came (the modularity audit's M06). Each editor still finds, marks and steps through matches its own way.

type Options = { searchIsCaseSensitive?: boolean, searchIsWholeWord?: boolean, searchIsRegexp?: boolean } | null | undefined

const keyOf = (value: string, opt: Options) => JSON.stringify([value ?? '', !!opt?.searchIsCaseSensitive, !!opt?.searchIsWholeWord, !!opt?.searchIsRegexp])

export class SearchSession {
    private latest: string | null = null

    /** This query is the one asked for now. */
    ask(value: string, opt: Options) {
        this.latest = keyOf(value, opt)
    }

    /** No search asked for (the bar emptied or closed). */
    clear() {
        this.latest = null
    }

    /** Whether this query is still the one asked for. */
    isLatest(value: string, opt: Options) {
        return this.latest !== null && this.latest === keyOf(value, opt)
    }
}
