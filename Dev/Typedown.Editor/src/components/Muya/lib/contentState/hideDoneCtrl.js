// Hide Done Items (services/hideDone.ts) as the editing sees it: a block folded away is one drawn inside a run marked done.
// The arrows go past it and Backspace does not join into it (arrowCtrl, backspaceCtrl).

const hideDoneCtrl = ContentState => {
  ContentState.prototype.isHiddenDone = function (block) {
    const element = block && document.getElementById(block.key)
    return !!element?.closest('.td-done')
  }
}

export default hideDoneCtrl
