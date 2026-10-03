// The page (WebView2) has TextEncoder; this test environment's jsdom does not.
import { TextEncoder } from 'util'
global.TextEncoder = TextEncoder
const Diagram = require('./plantuml').default

// PlantUML's hex encoding: what plantuml.com draws. The address used to end in "undefined" (no encoder at all).
test('a diagram is addressed by its UTF-8 text in hex', () => {
  expect(Diagram.encode('Alice -> Bob: 你好')).toBe('~h416c696365202d3e20426f623a20e4bda0e5a5bd')
})

test('the image points at plantuml.com with that text', () => {
  const div = document.createElement('div')
  Diagram.parse('A -> B').insertImgElement(div)
  expect(div.querySelector('img').getAttribute('src')).toBe('https://www.plantuml.com/plantuml/svg/~h41202d3e2042')
})
