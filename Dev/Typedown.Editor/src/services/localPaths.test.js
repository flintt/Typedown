import { absoluteImageUrls } from './localPaths'

const src = html => Array.from(new DOMParser().parseFromString(html, 'text/html').querySelectorAll('img')).map(i => i.getAttribute('src'))

test('a relative image is at a file:/// address in the copied HTML', () => {
  expect(src(absoluteImageUrls('<p><img src="images/a.png" alt="a"></p>', 'C:\\notes\\doc'))).toEqual(['file:///C:/notes/doc/images/a.png'])
  expect(src(absoluteImageUrls('<img src="../img/a.png">', 'C:\\notes\\doc'))).toEqual(['file:///C:/notes/img/a.png'])
  expect(src(absoluteImageUrls('<img src="images/a.png">', '/home/me/notes'))).toEqual(['file:///home/me/notes/images/a.png'])
})

test('names with spaces and #, absolute and network paths', () => {
  expect(src(absoluteImageUrls('<img src="b%20b.png">', 'C:\\n'))).toEqual(['file:///C:/n/b b.png'])
  expect(src(absoluteImageUrls('<img src="图/流程 图.png">', 'C:\\笔记'))).toEqual(['file:///C:/笔记/图/流程 图.png'])
  expect(src(absoluteImageUrls('<img src="c#1.png">', 'C:\\n'))).toEqual(['file:///C:/n/c%231.png'])
  expect(src(absoluteImageUrls('<img src="D:\\pics\\x.png">', 'C:\\n'))).toEqual(['file:///D:/pics/x.png'])
  expect(src(absoluteImageUrls('<img src="\\\\server\\share\\x.png">', 'C:\\n'))).toEqual(['file://server/share/x.png'])
})

test('web, data and file addresses stay; so does a relative one without a document folder', () => {
  const html = '<img src="https://e.com/a.png"><img src="data:image/png;base64,AAAA"><img src="file:///C:/a.png">'
  expect(src(absoluteImageUrls(html, 'C:\\n'))).toEqual(['https://e.com/a.png', 'data:image/png;base64,AAAA', 'file:///C:/a.png'])
  expect(src(absoluteImageUrls('<img src="a.png">', ''))).toEqual(['a.png'])
  expect(absoluteImageUrls('<p>no image</p>', 'C:\\n')).toBe('<p>no image</p>')
})
