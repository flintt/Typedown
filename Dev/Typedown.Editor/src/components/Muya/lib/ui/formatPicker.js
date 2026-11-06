import transport from 'services/transport'

class FormatPicker {
  static pluginName = 'formatPicker'
  constructor(muya) {
    muya.eventCenter.subscribe('muya-format-picker', ({ reference, ...arg }) => {
      if (reference) {
        const boundingClientRect = reference.getBoundingClientRect()
        // The formats by their names: the tokens themselves link back to their lists (parent), and the message could not
        // be written - "Converting circular structure to JSON" with text selected in a link or a bold word.
        const formats = (arg.formats || []).map(f => ({ type: f.type, tag: f.tag }))
        transport.postMessage('OpenFormatPicker', { ...arg, formats, boundingClientRect })
      }
    })
  }
}

export default FormatPicker
