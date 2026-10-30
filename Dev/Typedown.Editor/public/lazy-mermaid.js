// The editor draws Mermaid fences through window.mermaid (renderers/index.js, exportHtml.js: initialize, render, run).
// That is this wrapper, which draws them with Mermaid or with Pele (Settings > Editor > Diagram renderer;
// window.__typedownSetMermaidRenderer), loading the chosen one only when a document first draws a diagram:
// mermaid.min.js is 3.5 MB of script, and loading it from index.html made every start parse it first (about a quarter
// of a second) although few documents draw one (Tools/EditorBench/mermaid-lazy-check.js). The Pele side follows the
// Uno edition's adapter (Typedown-Uno, pele-mermaid-adapter.js).
(function () {
    'use strict';

    var engines = { mermaid: null, pele: null };
    var loading = { mermaid: null, pele: null };
    var renderer = 'mermaid';
    var options = { theme: 'default' };
    var nextId = 0;
    // Pele colours its drawing from these; written on the SVG itself, so a copy of it made into a picture (export,
    // the copy button) keeps them.
    var palettes = {
        default: {
            '--pele-bg': '#ffffff', '--pele-fg': '#24292e', '--pele-muted': '#6b7280',
            '--pele-line': '#374151', '--pele-surface': '#f3f4f6', '--pele-surface-alt': '#e5e7eb',
            '--pele-border': '#9ca3af', '--pele-accent': '#2563eb'
        },
        dark: {
            '--pele-bg': '#282828', '--pele-fg': '#f5f5f5', '--pele-muted': '#b0b0b0',
            '--pele-line': '#e5e7eb', '--pele-surface': '#383838', '--pele-surface-alt': '#454545',
            '--pele-border': '#888888', '--pele-accent': '#75b8ff'
        }
    };

    // A load that failed is tried again at the next diagram: otherwise every later one waited for a library that was
    // never coming.
    function load(name) {
        if (engines[name]) return Promise.resolve(engines[name]);
        if (!loading[name]) {
            loading[name] = new Promise(function (resolve, reject) {
                var script = document.createElement('script');
                script.src = name === 'pele' ? './pele.global.js' : './mermaid.min.js';
                script.onload = function () {
                    if (name === 'pele' && window.pele && typeof window.pele.render === 'function') engines.pele = window.pele;
                    if (engines[name]) resolve(engines[name]);
                    else reject(new Error(name + ' did not load'));
                };
                script.onerror = function () {
                    script.remove();
                    reject(new Error(name + ' could not be loaded'));
                };
                document.head.appendChild(script);
            }).catch(function (error) {
                loading[name] = null;
                throw error;
            });
        }
        return loading[name];
    }

    function peleSvg(pele, source, id, config, target) {
        var fontTarget = target || document.getElementById('ag-editor-id') || document.body;
        var result = pele.render(source, {
            idPrefix: String(id).replace(/[^a-zA-Z0-9_-]/g, '_'),
            fontFamily: getComputedStyle(fontTarget).fontFamily || 'sans-serif',
            // As Mermaid's securityLevel: strict: no link in a diagram is followed.
            links: false
        });
        var holder = document.createElement('div');
        holder.innerHTML = result.svg;
        var svg = holder.firstElementChild;
        if (!svg || svg.localName !== 'svg') throw new Error('Pele drew no SVG');
        // The editor gives a diagram's SVG width: 100%; Mermaid caps it at the drawing's own width, Pele only at the
        // page's, which blew a small drawing up to the whole width. Its own width at most, smaller in a narrow page.
        var width = /^\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*$/i.exec(svg.getAttribute('width') || '');
        var natural = width ? Number(width[1]) : svg.viewBox.baseVal.width;
        if (natural > 0 && Number.isFinite(natural)) svg.style.maxWidth = natural + 'px';
        var palette = palettes[config.theme === 'dark' ? 'dark' : 'default'];
        Object.keys(palette).forEach(function (key) { svg.style.setProperty(key, palette[key]); });
        return svg.outerHTML;
    }

    var wrapper = {
        initialize: function (config) {
            options = Object.assign({}, config || {});
            if (renderer === 'mermaid' && engines.mermaid) engines.mermaid.initialize(options);
        },
        render: async function (id, source) {
            var config = options;
            if (renderer === 'pele') {
                var pele = await load('pele');
                return { svg: peleSvg(pele, source, id || 'pele-' + nextId++, config) };
            }
            var mermaid = await load('mermaid');
            mermaid.initialize(config);
            return mermaid.render(id, source);
        },
        run: async function (config) {
            if (renderer === 'pele') {
                var settings = options;
                var nodes = config && config.nodes ? Array.from(config.nodes) : Array.from(document.querySelectorAll('.mermaid'));
                if (!nodes.length) return;
                var pele = await load('pele');
                for (var node of nodes) node.innerHTML = peleSvg(pele, node.textContent, 'pele-' + nextId++, settings, node);
                return;
            }
            var mermaid = await load('mermaid');
            mermaid.initialize(options);
            return mermaid.run(config);
        }
    };

    // mermaid.min.js ends with globalThis["mermaid"] = ...: that is caught as the engine, and window.mermaid stays the
    // wrapper (the editor keeps the reference it looked up).
    Object.defineProperty(window, 'mermaid', {
        configurable: true,
        enumerable: true,
        get: function () { return wrapper; },
        set: function (value) { if (value !== wrapper) engines.mermaid = value; }
    });

    // The renderer chosen in the settings: the diagrams drawn are drawn again (the editor keeps them by theme and
    // source, so its caches are emptied first).
    window.__typedownSetMermaidRenderer = function (value) {
        var next = value === 'pele' ? 'pele' : 'mermaid';
        if (next === renderer) return false;
        renderer = next;
        var muya = window.__typedownMuya;
        var state = muya && muya.contentState;
        if (!state || !state.stateRender) return true;
        var render = state.stateRender;
        render.mermaidRenderCache.clear();
        render.mermaidRenderTargets = new WeakMap();
        render.mermaidRenderRequests = new WeakMap();
        state.render(true, true);
        return true;
    };

    window.__typedownMermaidRenderer = function () { return renderer; };
})();
