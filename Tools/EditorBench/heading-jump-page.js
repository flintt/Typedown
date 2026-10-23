// The page's half of heading-jump-check.js and of the E2E case HJ02 (embedded there): defines window.__hjJump(at) - the
// host's outline jump (ScrollTo) to the first heading from `at` (a share of the blocks) on that is not on the page yet,
// then every frame where it is, until the drawing is done, the blocks guessed while drawn are predicted too (the
// pretext log's restDoneAt) and a second more - and window.__hjSummary(). Synchronous (the E2E driver does not await).
(() => {
  window.__hjJump = at => {
    const muya = window.__typedownMuya;
    const blocks = muya && muya.contentState.blocks;
    if (!blocks || !blocks.length) return { ok: false, why: 'no document' };
    let i = Math.floor(blocks.length * at);
    while (i < blocks.length && !(/^h[1-6]$/.test(blocks[i].type) && !document.getElementById(blocks[i].key))) i++;
    if (i >= blocks.length) {
      // Drawn whole already: the first heading from there on.
      i = Math.floor(blocks.length * at);
      while (i < blocks.length && !/^h[1-6]$/.test(blocks[i].type)) i++;
    }
    if (i >= blocks.length) return { ok: false, why: 'no heading' };
    const slug = blocks[i].key;
    const s = { slug, index: i, frames: [], t0: performance.now(), wasDrawing: !!muya.renderPending, done: false, restFrom: (window.__typedownPretextLog || {}).restDoneAt };
    window.__hjj = s;
    window.__typedownReceive('ScrollTo', { slug });
    const first = document.getElementById(slug);
    s.right = first ? first.getBoundingClientRect().top : null;
    let quietSince = null;
    const tick = () => {
      const el = document.getElementById(slug);
      const now = performance.now() - s.t0;
      s.frames.push([Math.round(now), el ? Math.round(el.getBoundingClientRect().top * 10) / 10 : null]);
      const log = window.__typedownPretextLog || {};
      const rested = window.__typedownPretextOff || (log.restDoneAt !== undefined && log.restDoneAt !== s.restFrom) || muya.contentState.stateRender.heights.resting === null;
      if (!muya.renderPending && rested) { if (quietSince === null) quietSince = now } else quietSince = null;
      if ((quietSince !== null && now - quietSince > 1000) || now > 20000) { s.done = true; return }
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
    return { ok: true, slug, index: i, of: blocks.length, wasDrawing: s.wasDrawing };
  };
  window.__hjSummary = () => {
    const s = window.__hjj;
    const tops = s.frames.filter(f => f[1] !== null);
    let moves = 0;
    for (let k = 1; k < tops.length; k++) if (Math.abs(tops[k][1] - tops[k - 1][1]) > 1) moves++;
    const target = 16;
    const end = tops.length ? tops[tops.length - 1][1] : null;
    return {
      ok: true, index: s.index, wasDrawing: s.wasDrawing, rightAfter: s.right === null ? null : Math.round(s.right - target),
      firstFrame: tops.length ? Math.round(tops[0][1] - target) : null, endOff: end === null ? null : Math.round(Math.abs(end - target)),
      long: !!document.querySelector('#editor.ag-long-document'), worstOff: tops.length ? Math.round(Math.max(...tops.map(f => Math.abs(f[1] - target)))) : null, moves, watchedMs: s.frames.length ? s.frames[s.frames.length - 1][0] : 0,
    };
  };
  return 'ok';
})()
