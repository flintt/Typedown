import { remote } from 'services/remote';
import transport from 'services/transport';

const names = [
    'InputFootnoteDefine',
    'InputYAMLFrontMatter',
    'InputMathFormula',
    'InputLanguageIdentifier',
    'ClickToAddAnImage',
    'LoadImageFail',
    'Footnote'
]

// These end up as CSS variables the editor's own placeholders read, so they are fetched rather than bound.
// The host says when the interface language changed; without that they would keep the language the editor
// started in, which is the one thing in the window that a language change used to leave behind.
const load = () => remote.getStringResources({ names }).then(dic => {
    for (const key in dic) {
        document.documentElement.style.setProperty(`--${key}`, `'${dic[key]}'`)
    }
});

load();

transport.addListener('LanguageChanged', () => { load(); });
