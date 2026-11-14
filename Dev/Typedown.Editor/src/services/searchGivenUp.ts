// Whether the last search's regular expression was given up on, too slow on the texts (services/common trySearch): the
// find bar says so instead of a bare "no results" (services/findResults sends it with the count). On its own, for both
// to read without either taking the other in.
let givenUp = false

export const setSearchGivenUp = (value: boolean) => { givenUp = value }

export const searchGivenUp = () => givenUp
