'use strict';
// Static local parser only: no evaluated input, shell, resource loading or HTML output.
let katex;
try {
  katex = require('katex');
  if (katex.version !== '0.16.47') process.exit(2);
} catch { process.exit(2); }
const fs = require('node:fs');
try {
  const expressions = JSON.parse(fs.readFileSync(0, 'utf8'));
  const answers = expressions.map(({source, display}) => {
    let unsafe = false;
    try {
      if (!source.trim()) return false;
      katex.renderToString(source, {
        displayMode: display, throwOnError: true, strict: 'error',
        trust: () => { unsafe = true; return false; },
        maxExpand: 1000, maxSize: 100, globalGroup: false, macros: {},
      });
      return !unsafe;
    } catch { return false; }
  });
  process.stdout.write(JSON.stringify(answers));
} catch { process.exit(1); }
