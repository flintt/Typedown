// A WebP or AVIF picture drawn as PNG by the page for the Word export (ConvertPicture -> PictureConverted; the audit of
// 2026-11-24, F1: both were left out of every DOCX). The host sends the picture's bytes as a data: address - not its
// address, so a picture from another site taints no canvas - and gets a PNG of the same size back; bytes that are no
// picture get an error, not a hang.
//
//   node picture-convert-check.js
const fs = require('fs');
const path = require('path');
const { openEditor } = require('./harness');

(async () => {
    const { page, errors, mode, close } = await openEditor();
    let ok = true;
    const check = (c, label, detail) => { console.log(`${c ? 'PASS' : 'FAIL'} ${label}${detail !== undefined ? ' - ' + JSON.stringify(detail) : ''}`); ok = ok && c; };
    const fixtures = path.join(__dirname, 'fixtures');
    let token = 700;
    const convert = (data) => page.evaluate(({ data, token }) => new Promise(resolve => {
        delete window.__last.PictureConverted;
        window.__deliver('ConvertPicture', { token, data });
        const started = Date.now();
        const wait = () => {
            const reply = window.__last.PictureConverted;
            if (reply && reply.token === token) return resolve(reply);
            if (Date.now() - started > 8000) return resolve({ timedOut: true });
            setTimeout(wait, 50);
        };
        wait();
    }), { data, token: ++token });
    try {
        await mode(false, false);
        for (const [file, mime] of [['pixel.webp', 'image/webp'], ['pixel.avif', 'image/avif']]) {
            const bytes = fs.readFileSync(path.join(fixtures, file));
            const reply = await convert(`data:${mime};base64,${bytes.toString('base64')}`);
            const png = reply.png ? Buffer.from(reply.png, 'base64') : null;
            const isPng = !!png && png.slice(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
            const size = isPng ? [png.readUInt32BE(16), png.readUInt32BE(20)] : null;
            check(isPng && size[0] === 8 && size[1] === 8, `${file} comes back as an 8 x 8 PNG`, { size, error: reply.error, timedOut: reply.timedOut });
        }
        const bad = await convert('data:image/webp;base64,' + Buffer.from('not a picture').toString('base64'));
        check(!bad.timedOut && !bad.png && !!bad.error, 'bytes that are no picture: an error back, not a hang', bad);
        check(errors.length === 0, 'no page errors', errors);
    } catch (e) {
        check(false, 'ran to the end', String(e && e.stack || e));
    } finally {
        await close();
        process.exit(ok ? 0 : 1);
    }
})();
