import '../config' // first: config and utils import each other, and config needs utils done
import { tokenizer } from './index'

// A code span binds tighter than the marks around it (GFM): "~~" or "**" inside one closes nothing outside it.
const shape = tokens => tokens.map(t => t.children ? `${t.type}(${shape(t.children)})` : t.type === 'text' ? `text:${t.content}` : `${t.type}:${t.content ?? t.raw}`).join(' ')

test('strikethrough around a code span holding "~~"', () => {
  expect(shape(tokenizer('~~符号前`~~, ** `符号后~~'))).toBe('del(text:符号前 inline_code:~~, **  text:符号后)')
})

test('highlight around a code span holding "=="', () => {
  expect(shape(tokenizer('==a `==` b=='))).toBe('mark(text:a  inline_code:== text: b)')
})

test('bold around a code span holding "**"', () => {
  expect(shape(tokenizer('**a `**` b**'))).toBe('strong(text:a  inline_code:** text: b)')
})

test('a strikethrough closed before a code span is left as it was', () => {
  expect(shape(tokenizer('~~a~~ `~~` b'))).toBe('del(text:a) text:  inline_code:~~ text: b')
})

test('no closer outside the code span: no strikethrough', () => {
  expect(shape(tokenizer('~~a `b~~`'))).toBe('text:~~a  inline_code:b~~')
})
