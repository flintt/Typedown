import { classifyNormalization, extractProtectedPayload } from './normalization';

const verdict = (source: string, normalized: string) => classifyNormalization(source, normalized).pendingNormalization;
const reasons = (source: string, normalized: string) => classifyNormalization(source, normalized).reasons;

test('identical text is none', () => {
    expect(verdict('# a\n\ntext\n', '# a\n\ntext\n')).toBe('none');
    expect(verdict('', '')).toBe('none');
});

test('pure formatting changes are unknown, never safe by default', () => {
    expect(verdict('* a\n* b\n', '- a\n- b\n')).toBe('unknown');
    expect(verdict('Title\n=====\n', '# Title\n')).toBe('unknown');
    expect(verdict('~~~py\nx = 1\n~~~\n', '```py\nx = 1\n```\n')).toBe('unknown');
    expect(verdict('a\n\n\n\nb\n', 'a\n\nb\n')).toBe('unknown');
    expect(reasons('* a\n', '- a\n')).toEqual(['formatting-changed']);
});

test('a reference link turned inline keeps its destination and title', () => {
    expect(verdict('[x][r]\n\n[r]: https://e.com/a "T"\n', '[x](https://e.com/a "T")\n')).toBe('unknown');
});

test('a fence info string or body that changes is unsafe', () => {
    expect(verdict('```ts {title="x.ts"}\nconst a = 1;\n```\n', '```ts\nconst a = 1;\n```\n')).toBe('unsafe');
    expect(reasons('```js\nlet a\n```\n', '```js\nlet b\n```\n')).toContain('fence-lost');
    // An indented fence strips that indentation from its content, so dropping both loses nothing.
    expect(verdict(' ```\n aaa\naaa\n```\n', '```\naaa\naaa\n```\n')).toBe('unknown');
    expect(verdict('  ```\n   aaa\n  ```\n', '```\naaa\n```\n')).toBe('unsafe');
});

test('the reading-mode corruption fixed in 7c02d10 would be caught', () => {
    const source = '## 提交和拉取说明\n```bash\n# git branch\n* test\n# git remote\norigin\n```\n';
    const corrupted = '- `test`\n  ## 提交和拉取说明\n  ```bash\n  # git branch\n* test\n  # git remote\n  origin\n  ```\n';
    const result = classifyNormalization(source, corrupted);
    expect(result.pendingNormalization).toBe('unsafe');
    expect(result.reasons).toContain('fence-lost');
});

test('links, footnotes, task states, raw HTML, front matter and maths are protected', () => {
    expect(reasons('[a](https://e.com/x)\n', '[a](https://e.com/y)\n')).toContain('link-lost');
    expect(reasons('![p](a.png "cap")\n', '![p](a.png)\n')).toContain('link-lost');
    expect(reasons('<https://auto.link>\n', 'https://auto.link\n')).toContain('link-lost');
    expect(reasons('a[^n]\n\n[^n]: note\n', 'a[^n]\n')).toContain('footnote-lost');
    expect(reasons('- [x] done\n', '- [ ] done\n')).toContain('task-state-changed');
    expect(verdict('- [X] done\n', '- [x] done\n')).toBe('unknown');
    expect(reasons('<div align="center" data-x="1">y</div>\n', '<div align="center">y</div>\n')).toContain('html-lost');
    expect(verdict('<div data-x="1" align="center">y</div>\n', '<div align="center" data-x="1">y</div>\n')).toBe('unknown');
    expect(reasons('---\ntitle: a\n---\n\nx\n', '---\ntitle: b\n---\n\nx\n')).toContain('front-matter-lost');
    expect(reasons('$$\nx^2\n$$\n', '$$\nx^3\n$$\n')).toContain('math-lost');
    expect(reasons('a $\\alpha$ b\n', 'a $\\beta$ b\n')).toContain('math-lost');
    expect(extractProtectedPayload('costs $5 and $6, `$x$` in code\n').math).toEqual([]);
    expect(extractProtectedPayload('a $x^2$ and $$ not inline\n').math).toEqual(['inline:x^2']);
});

test('a lost word is unsafe; added text alone is unknown', () => {
    expect(reasons('alpha beta gamma\n', 'alpha gamma\n')).toContain('word-lost');
    expect(reasons('中文内容\n', '中文\n')).toContain('word-lost');
    const added = classifyNormalization('alpha\n', 'alpha alpha\n');
    expect(added.pendingNormalization).toBe('unknown');
    expect(added.reasons).toContain('text-added');
});

test('payload extraction ignores markup inside fences and reads the front matter', () => {
    const payload = extractProtectedPayload('---\nk: v\n---\n\n```md\n[a](b)\n- [x] t\n```\n\n[c](d)\n');
    expect(payload.frontMatter).toEqual(['k: v']);
    expect(payload.fences).toEqual(['md\u0000[a](b)\n- [x] t']);
    expect(payload.links).toEqual(['d\u0000']);
    expect(payload.tasks).toEqual([]);
});

describe('an ordered list numbered on is not a word lost', () => {
    const safe = (source: string, normalized: string) => classifyNormalization(source, normalized).reasons.includes('word-lost')
    it('a skipped number', () => {
        expect(safe('1. a\n2. b\n10. c\n', '1. a\n2. b\n3. c\n')).toBe(false)
    })
    it('a list starting at 3', () => {
        expect(safe('3. a\n7. b\n', '3. a\n4. b\n')).toBe(false)
    })
    it('the "1)" way, nested and in a quotation', () => {
        expect(safe('1) a\n5) b\n\n> 1. q\n> 9. r\n\n- x\n  1. y\n  4. z\n', '1) a\n2) b\n\n> 1. q\n> 2. r\n\n- x\n  1. y\n  2. z\n')).toBe(false)
    })
    it('a number in the text is still a word', () => {
        expect(safe('1. it costs 10 yuan\n', '1. it costs yuan\n')).toBe(true)
    })
})
