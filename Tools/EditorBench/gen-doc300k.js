// doc300k.md, the 300,000-character document some checks open (keep-source-on-save-check, progressive-apply-check,
// outline-load-check, meaning-check, the harness's large-doc fixture): sections of a heading, a paragraph, a list, a code
// block, a table and a quotation, as many as make it 300,000 characters. Not kept in the repository; written here when
// a check needs it and it is not there.
//
//   node gen-doc300k.js        (writes doc300k.md beside it)
const fs = require('fs');
const path = require('path');

function doc300k() {
    let text = '';
    for (let i = 1; text.length < 300000; i++) {
        text += `## Section ${i}\n\nParagraph ${i} with **bold**, *italic* and \`code\`, long enough to wrap onto a second line in any reasonable editor width.\n\n` +
            `- one\n- two\n- three\n\n\`\`\`js\nconst a = ${i};\nconsole.log(a * 2);\n\`\`\`\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n> a quotation\n\n`;
    }
    return text.replace(/\n+$/, '\n');
}

/** The file's path, written first if it is not there. */
function ensureDoc300k() {
    const file = path.join(__dirname, 'doc300k.md');
    if (!fs.existsSync(file)) fs.writeFileSync(file, doc300k());
    return file;
}

module.exports = { doc300k, ensureDoc300k };
if (require.main === module) console.log(ensureDoc300k());
