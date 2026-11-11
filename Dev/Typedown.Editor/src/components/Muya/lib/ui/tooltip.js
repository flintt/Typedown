import transport from 'services/transport'

class Tooltip {
  constructor(muya) {
    this.muya = muya
    this.cache = new WeakMap()
    const { eventCenter } = this.muya
    eventCenter.attachDOMEvent(document, 'mouseover', this.mouseOver.bind(this))
  }

  mouseOver(event) {
    // Not from an editor that is only being kept in memory: it would report a tooltip to the host for a
    // document nobody is looking at.
    if (!this.muya.container.isConnected) return
    const { target } = event
    const toolTipTarget = target.closest('[data-tooltip]')
    if (toolTipTarget && !this.cache.has(toolTipTarget)) {
      const { eventCenter } = this.muya
      const eventId = eventCenter.attachDOMEvent(toolTipTarget, 'mouseleave', this.mouseLeave.bind(this))
      const tooltip = toolTipTarget.getAttribute('data-tooltip')
      const boundingClientRect = toolTipTarget.getBoundingClientRect()
      transport.postMessage('OpenToolTip', { open: true, tooltip, boundingClientRect })
      // Watching for the target taken out of the page while the pointer is on it; cleared when the pointer leaves too -
      // it was cleared only when the target went, one more 300 ms timer at every hover (the audit's P06).
      const timer = setInterval(() => {
        if (!document.body.contains(toolTipTarget)) this.mouseLeave({ target: toolTipTarget })
      }, 300)
      this.cache.set(toolTipTarget, { eventId, timer })
    }
  }

  mouseLeave({ target }) {
    if (this.cache.has(target)) {
      const { eventCenter } = this.muya
      const { eventId, timer } = this.cache.get(target)
      clearInterval(timer)
      eventCenter.detachDOMEvent(eventId)
      transport.postMessage('OpenToolTip', { open: false });
      this.cache.delete(target)
    }
  }
}

export default Tooltip
