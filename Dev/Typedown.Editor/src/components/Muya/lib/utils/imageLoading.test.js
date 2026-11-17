// Match the editor's configuration-first initialization of Muya's existing config/utils cycle.
import '../config'
import { checkImageContentType } from './index'

test('extensionless HTTP drag detection uses the same image-only route as displayed images', async () => {
  const originalImage = window.Image
  const loaded = []
  window.httpImageEndpoint = 'https://local-images.invalid/__http_image'
  window.Image = class {
    set src (value) { loaded.push({ value, cors: this.crossOrigin }); this.onload() }
  }
  try {
    const original = 'http://192.168.1.20/image?signature=a%2Bb'
    expect(await checkImageContentType(original)).toBe(true)
    expect(new URL(loaded[0].value).searchParams.get('src')).toBe(original)
    expect(loaded[0].value).toMatch(/^https:\/\/local-images.invalid\/__http_image\?/)
    expect(loaded[0].cors).toBe('anonymous')
  } finally {
    window.Image = originalImage
    delete window.httpImageEndpoint
  }
})
