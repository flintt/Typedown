// The keys the page acts on itself that the host lets a person change (Settings > Shortcuts; SettingsViewModel.KeyBindings):
// each as Windows' virtual key code - which is KeyboardEvent.keyCode in the page - and its modifiers, or null when
// cleared. Without the host's (the page alone, a test page), the keys these did before they could be changed.
export const DEFAULT_KEY_BINDINGS = {
  indent: { keyCode: 9, ctrl: false, alt: false, shift: false },
  outdent: { keyCode: 9, ctrl: false, alt: false, shift: true },
  softLineBreak: { keyCode: 13, ctrl: false, alt: false, shift: true },
  cellLineBreak: { keyCode: 13, ctrl: false, alt: true, shift: false },
  documentStart: { keyCode: 36, ctrl: true, alt: false, shift: false },
  documentEnd: { keyCode: 35, ctrl: true, alt: false, shift: false },
  previewImage: { keyCode: 32, ctrl: false, alt: false, shift: false },
  // A click with these held (the key code stands for the click: ShortcutKey.Click).
  openLink: { keyCode: 0xE9, ctrl: true, alt: false, shift: false },
  leaveTable: { keyCode: 13, ctrl: true, alt: false, shift: false },
  undo: { keyCode: 90, ctrl: true, alt: false, shift: false },
  redo: { keyCode: 89, ctrl: true, alt: false, shift: false },
  selectAll: { keyCode: 65, ctrl: true, alt: false, shift: false }
}

/** The binding of one of them: the host's, else the default. */
export const keyBinding = (options, name) => {
  const bindings = options && options.keyBindings
  return bindings && Object.prototype.hasOwnProperty.call(bindings, name) ? bindings[name] : DEFAULT_KEY_BINDINGS[name]
}

/** Whether a key event is that binding: the same key, exactly these modifiers. */
export const matchesKey = (event, binding) =>
  !!binding &&
  event.keyCode === binding.keyCode &&
  !!event.ctrlKey === !!binding.ctrl &&
  !!event.altKey === !!binding.alt &&
  !!event.shiftKey === !!binding.shift &&
  !event.metaKey

/**
 * Whether a mouse event is that click binding: its modifiers held (others may be too, as Ctrl+Shift+click opened a link
 * before the key could be changed; Cmd counts as Ctrl). None cleared or none chosen: never.
 */
export const matchesClick = (event, binding) =>
  !!binding && !!(binding.ctrl || binding.alt || binding.shift) &&
  (!binding.ctrl || !!event.ctrlKey || !!event.metaKey) &&
  (!binding.alt || !!event.altKey) &&
  (!binding.shift || !!event.shiftKey)

/**
 * The event as the handler that does the work expects it (Tab for an indent, Shift+Enter for a line break) when another
 * key was given to it: these properties read as given, everything else - preventDefault() among it - is the event's.
 */
export const asKeys = (event, overrides) => new Proxy(event, {
  get: (target, name) => {
    if (Object.prototype.hasOwnProperty.call(overrides, name)) return overrides[name]
    const value = target[name]
    return typeof value === 'function' ? value.bind(target) : value
  }
})
