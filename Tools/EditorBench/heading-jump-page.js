// The page's half of heading-jump-check.js: defines window.__hjJump(at) - the host's outline jump (ScrollTo) to the first
// heading from `at` (a share of the blocks) on that is not on the page yet - then, every frame, where that heading is,
// until the drawing is done and a second more (at least a second after the jump) - and window.__hjSummary().
// Synchronous (an E2E driver need not await it).
(() => {
  // The jump waits for the load to have put the page where it belongs (its caret placed, Muya loadCursorPending): a jump
  // before that was undone by the load's own scroll to the top.
  window.__hjJump = at => {
    const muya = window.__typedownMuya;
    const blocks = muya && muya.contentState.blocks;
    if (!blocks || !blocks.length) return { ok: false, why: 'no document' };
    if (muya.loadCursorPending) {
      window.__hjj = { done: false, waiting: true };
      const wait = () => {
        if (muya.loadCursorPending) return requestAnimationFrame(wait);
        const r = window.__hjJumpNow(at);
        if (!r.ok) window.__hjj = { done: true, result: r };
      };
      requestAnimationFrame(wait);
      return { ok: true, deferred: true };
    }
    return window.__hjJumpNow(at);
  };
  window.__hjJumpNow = at => {
    const muya = window.__typedownMuya;
    const blocks = muya.contentState.blocks;
    let i = Math.floor(blocks.length * at);
    while (i < blocks.length && !(/^h[1-6]$/.test(blocks[i].type) && !document.getElementById(blocks[i].key))) i++;
    if (i >= blocks.length) {
      // Drawn whole already: the first heading from there on.
      i = Math.floor(blocks.length * at);
      while (i < blocks.length && !/^h[1-6]$/.test(blocks[i].type)) i++;
    }
    if (i >= blocks.length) return { ok: false, why: 'no heading' };
    const slug = blocks[i].key;
    const s = { slug, index: i, frames: [], t0: performance.now(), wasDrawing: !!muya.renderPending, done: false };
    window.__hjj = s;
    (window.__typedownReceive || window.__deliver)('ScrollTo', { slug });
    const first = document.getElementById(slug);
    s.right = first ? first.getBoundingClientRect().top : null;
    let quietSince = null;
    const tick = () => {
      const el = document.getElementById(slug);
      const now = performance.now() - s.t0;
      s.frames.push([Math.round(now), el ? Math.round(el.getBoundingClientRect().top * 10) / 10 : null, !!muya.renderPending, Math.round(window.scrollY)]);
      if (!muya.renderPending) { if (quietSince === null) quietSince = now } else quietSince = null;
      if ((quietSince !== null && now - quietSince > 1000 && now > 1500) || now > 20000) { s.done = true; return }
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
    return { ok: true, slug, index: i, of: blocks.length, wasDrawing: s.wasDrawing };
  };
  window.__hjSummary = () => {
    const s = window.__hjj;
    if (s.result) return s.result;
    const tops = s.frames.filter(f => f[1] !== null);
    let moves = 0;
    const moved = [];
    for (let k = 1; k < tops.length; k++) {
      const d = tops[k][1] - tops[k - 1][1];
      if (Math.abs(d) > 1) { moves++; if (moved.length < 8) moved.push([tops[k][0], Math.round(d), 'scrollY ' + tops[k - 1][3] + '>' + tops[k][3], tops[k][2] ? 'drawing' : 'drawn']); }
    }
    const target = 16;
    const end = tops.length ? tops[tops.length - 1][1] : null;
    return {
      ok: true, index: s.index, wasDrawing: s.wasDrawing, rightAfter: s.right === null ? null : Math.round(s.right - target),
      firstFrame: tops.length ? Math.round(tops[0][1] - target) : null, endOff: end === null ? null : Math.round(Math.abs(end - target)),
      long: !!document.querySelector('#editor.ag-long-document'), worstOff: tops.length ? Math.round(Math.max(...tops.map(f => Math.abs(f[1] - target)))) : null,
      missingFrames: s.frames.length - tops.length, moves, moved, watchedMs: s.frames.length ? s.frames[s.frames.length - 1][0] : 0,
      drawnAtMs: (s.frames.find(f => !f[2]) || [null])[0],
    };
  };
  return 'ok';
})()
