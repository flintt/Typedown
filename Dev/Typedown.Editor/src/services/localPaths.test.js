import { absoluteImageUrls, withResolution, svgImagesAsPng, localImageUrl, localImageSource, httpImageUrl, httpImageSource } from './localPaths'

afterEach(() => { delete window.localImageEndpoint; delete window.httpImageEndpoint })

test('HTTP images use their own display route, and copied HTML restores the original signed address', () => {
  window.httpImageEndpoint = 'https://local-images.invalid/__http_image'
  const original = 'http://lan.example/picture?signature=a%2Bb&name=图'
  const url = httpImageUrl(original)
  expect(httpImageSource(url)).toBe(original)
  expect(httpImageUrl('https://example.com/a.png')).toBeNull()
  expect(httpImageSource(url.replace('local-images.invalid/', 'evil.example/'))).toBeNull()
  expect(src(absoluteImageUrls(`<img src="${url}">`))).toEqual([original])
})

test('the host image route carries an absolute path for each document, decoded only once', () => {
  window.localImageEndpoint = 'https://editor.invalid/__local_image'
  const source = (value, base = 'C:\\notes\\sub') => new URL(localImageUrl(value, base)).searchParams.get('src')
  expect(source('../图/a%20b.png')).toBe('C:/notes/图/a b.png')
  expect(source('D:\\pictures\\a + b#1.png')).toBe('D:\\pictures\\a + b#1.png')
  expect(source('file:///C:/notes/a%23b.png')).toBe('C:/notes/a#b.png')
  expect(source('file://server/share/a%20b.png')).toBe('//server/share/a b.png')
  expect(source('images/%2523.png')).toBe('C:/notes/sub/images/%23.png')
  expect(source('images/a.png', 'D:\\other')).toBe('D:/other/images/a.png')
  expect(localImageUrl('https://example.com/a.png')).toBeNull()
  expect(localImageUrl('data:image/png;base64,AAAA')).toBeNull()
  expect(localImageUrl('javascript:alert(1)')).toBeNull()
})

test('browser-only hosts keep their paths, and copied HTML never exposes the private image route', () => {
  expect(localImageUrl('images/a.png', 'C:\\notes')).toBeNull()
  window.localImageEndpoint = 'https://editor.invalid/__local_image'
  const url = localImageUrl('../a%20b.svg', 'C:\\notes\\sub')
  expect(localImageSource(url + '&msec=1')).toBe('C:/notes/a b.svg')
  expect(localImageSource(url.replace('editor.invalid/', 'evil.example/'))).toBeNull()
  expect(src(absoluteImageUrls(`<img src="${url}">`))).toEqual(['file:///C:/notes/a b.svg'])
})

test('hosted local images preserve literal percent filenames through copied file URLs', () => {
  window.localImageEndpoint = 'https://local-images.invalid/__local_image'
  const url = localImageUrl('images/%2520%2523.svg', 'C:/notes')
  expect(localImageSource(url)).toBe('C:/notes/images/%20%23.svg')
  expect(src(absoluteImageUrls(`<img src="${url}">`))).toEqual(['file:///C:/notes/images/%2520%2523.svg'])
})

test('HTTP SVG clipboard conversion matches the full original URL, including case and query', () => {
  window.httpImageEndpoint = 'https://local-images.invalid/__http_image'
  const original = 'http://lan.example/Icon.svg?token=A%2Bb'
  const image = document.createElement('img')
  image.src = httpImageUrl(original) + '&msec=123'
  Object.defineProperties(image, { complete: { value: true }, naturalWidth: { value: 20 }, naturalHeight: { value: 10 } })
  const page = {
    querySelectorAll: () => [image],
    createElement: tag => tag === 'canvas' ? {
      getContext: () => ({ drawImage: jest.fn() }),
      toDataURL: () => 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
    } : document.createElement(tag)
  }
  const copied = `<img src="${original}">`
  expect(src(svgImagesAsPng(copied, page))[0]).toMatch(/^data:image\/png;base64,/)
  expect(src(svgImagesAsPng(copied.replace('token=A', 'token=a'), page))[0]).toBe(original.replace('token=A', 'token=a'))
  expect(src(svgImagesAsPng(copied.replace('Icon.svg', 'icon.svg'), page))[0]).toBe(original.replace('Icon.svg', 'icon.svg'))
  window.localImageEndpoint = 'https://local-images.invalid/__local_image'
  image.src = localImageUrl('Icon.svg', 'C:/notes') + '&msec=123'
  expect(src(svgImagesAsPng('<img src="file:///C:/notes/Icon.svg">', page))[0]).toMatch(/^data:image\/png;base64,/)
})

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

test('a file address written encoded goes as the path it names', () => {
  expect(src(absoluteImageUrls('<img src="file:///C:/%E5%9B%BE/a%20b.png">', 'C:\\n'))).toEqual(['file:///C:/图/a b.png'])
  expect(src(absoluteImageUrls('<img src="file:///home/me/%E5%9B%BE.png">', ''))).toEqual(['file:///home/me/图.png'])
  expect(src(absoluteImageUrls('<img src="file://server/share/a%20b.png">', ''))).toEqual(['file://server/share/a b.png'])
  expect(src(absoluteImageUrls('<img src="file:///C:/x/ja-dark[1].png">', ''))).toEqual(['file:///C:/x/ja-dark[1].png'])
})

test('web, data and file addresses stay; so does a relative one without a document folder', () => {
  const html = '<img src="https://e.com/a.png"><img src="data:image/png;base64,AAAA"><img src="file:///C:/a.png">' // a file address with nothing to decode stays the same
  expect(src(absoluteImageUrls(html, 'C:\\n'))).toEqual(['https://e.com/a.png', 'data:image/png;base64,AAAA', 'file:///C:/a.png'])
  expect(src(absoluteImageUrls('<img src="a.png">', ''))).toEqual(['a.png'])
  expect(absoluteImageUrls('<p>no image</p>', 'C:\\n')).toBe('<p>no image</p>')
})

test('a PNG gets its resolution: a pHYs chunk right after the header, with a valid CRC', () => {
  // 1x1 PNG
  const png = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
  const out = Uint8Array.from(atob(withResolution(png, 2).split(',')[1]), c => c.charCodeAt(0))
  const view = new DataView(out.buffer)
  expect(String.fromCharCode(...out.subarray(37, 41))).toBe('pHYs')
  expect(view.getUint32(41)).toBe(7559) // 192 dpi in pixels per metre
  expect(out[49]).toBe(1)
  expect(view.getUint32(50)).toBe(0x8fe5f165) // zlib.crc32(b"pHYs" + data), computed apart
  expect(String.fromCharCode(...out.subarray(58, 62))).toBe('IDAT')
})
