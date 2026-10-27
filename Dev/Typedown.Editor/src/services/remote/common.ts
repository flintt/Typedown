import { remoteFunction } from '../transport'

const remote = {
    getCurrentTheme: remoteFunction<undefined, string>('GetCurrentTheme'),
    contentLoaded: remoteFunction<undefined, string>('ContentLoaded'),
    // An export's HTML, or why it could not be made (the host says it).
    exportCallback: remoteFunction<{ html?: string, error?: string, context: unknown }, boolean>('ExportCallback', 0),
    printHTML: remoteFunction<{ html?: string, error?: string, context: unknown }, boolean>('PrintHTML', 0),
    // This page about to be printed (true), or the print preview closed (false): the host makes the margins around
    // the printed page white meanwhile (they were the web view's colour, the theme's).
    printing: remoteFunction<{ printing: boolean }, boolean>('Printing'),
    // No time limit: the host answers when the reader closes its dialog, however long that takes (after 30 s the
    // answer went nowhere and OK resized nothing).
    resizeTable: remoteFunction<{ rows: number, columns: number }, { rows: number, columns: number } | null>('ResizeTable', 0),
    loadImage: remoteFunction<{ url: string, width: number, height: number }, { url: string }>('LoadImage'),
    // No time limit: at start-up the host may wait on a dialog (recover a backup?) before it answers; after 30 s the
    // page gave up and the editor stayed blank.
    getSettings: remoteFunction<undefined, unknown>('GetSettings', 0),
    setClipboard: remoteFunction<{ type: string, data: unknown }, boolean>('SetClipboard'),
    // A figure's copy button (a diagram's, a display formula's, a table's): its Markdown and, for the programs that
    // take more, a picture (a PNG, base64) or HTML (a table's) - null when it has none - as one clipboard item. True
    // once the clipboard holds them.
    copyBlock: remoteFunction<{ text: string, png: string | null, html: string | null }, boolean>('CopyBlock'),
    getStringResources: remoteFunction<{ names: string[] }, { [name: string]: string }>('GetStringResources'),
    openNewWindow: remoteFunction<string, undefined>('OpenNewWindow'),
    unhandledException: remoteFunction<string, undefined>('UnhandledException'),
}

export default remote;