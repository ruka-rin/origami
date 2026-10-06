/*
用旧实现渲染单字母，产出 SVG 作为基准。用完即删。

关键：showFolded 对每个字母直接用 letter.folded 的多边形，
**不做任何额外变换** —— 所以旧代码画出来的形态就是 foldStrip 的原始输出。
*/
'use strict';
const fs = require('fs');
const path = require('path');
const os = require('os');

const ROOT = path.resolve(__dirname, '..');
const SRC = path.join(ROOT, '..', '旧的参考代码', 'strip.js');
const src = fs.readFileSync(SRC, 'utf-8');

function scan(s, from, open, close) {
  let depth = 0, inStr = null;
  for (let i = from; i < s.length; i++) {
    const c = s[i];
    if (inStr) {
      if (c === '\\') { i++; continue; }
      if (c === inStr) inStr = null;
      continue;
    }
    if (c === '"' || c === "'") { inStr = c; continue; }
    if (c === open) depth++;
    else if (c === close) { depth--; if (depth === 0) return [from, i + 1]; }
  }
}

function grab(name) {
  const i = src.indexOf(name + ' = ');
  if (name === 'fontEnc') {
    const [b, e] = scan(src, src.indexOf('{', i), '{', '}');
    return eval('(' + src.slice(b, e) + ')');
  }
  const fn = src.indexOf('function', i);
  const [, e] = scan(src, src.indexOf('{', fn), '{', '}');
  return eval('(' + src.slice(fn, e) + ')');
}

const fontEnc = grab('fontEnc');
const parseEnc = grab('parseEnc');
const makePoint = grab('makePoint');
const reflectPoint = grab('reflectPoint');
const reflectPolygon = grab('reflectPolygon');
const rotatePoint = grab('rotatePoint');
const rotatePolygon = grab('rotatePolygon');
const foldStrip = grab('foldStrip');

/** 复刻 index.js 的行编码（单字符，left=0，走 start 变体） */
function encodeOne(ch) {
  let key = ch.toUpperCase();
  if ((key + 'start') in fontEnc) key = key + 'start';
  return fontEnc[key].replace(/^[ _]/, '');
}

/** 旧实现的 letter.folded：foldStrip(parseEnc(编码)) */
function foldedOf(ch) {
  const letter = parseEnc(encodeOne(ch), false);
  return { rotate: letter.rotate, polygons: foldStrip(letter) };
}

/** 照showFolded 的做法直接输出 SVG（y 向下，不做任何额外变换） */
function svgOf(polygons, unit = 22, pad = 8) {
  const pts = polygons.flat();
  const x0 = Math.min(...pts.map(p => p.x)), x1 = Math.max(...pts.map(p => p.x));
  const y0 = Math.min(...pts.map(p => p.y)), y1 = Math.max(...pts.map(p => p.y));
  const W = (x1 - x0) * unit + 2 * pad, H = (y1 - y0) * unit + 2 * pad;
  // showFolded 只做 g.translate(x - bbox.x, ...)，即平移到画布内
  const Sx = p => pad + (p.x - x0) * unit;
  const Sy = p => pad + (p.y - y0) * unit;

  let body = '';
  polygons.forEach((poly, i) => {
    const d = poly.map(p => `${Sx(p).toFixed(1)},${Sy(p).toFixed(1)}`).join(' ');
    body += `<polygon points="${d}" fill="#f5d949" stroke="#333" stroke-width="1"/>`;
  });
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${W.toFixed(0)} ${H.toFixed(0)}" ` +
         `width="${W.toFixed(0)}" height="${H.toFixed(0)}">` +
         `<rect width="100%" height="100%" fill="#fff"/>${body}</svg>`;
}

const out = {};
const letters = process.argv[2] || 'VLOABCDEFGHIJKMNPQRSTUWXYZ01568S';
for (const ch of letters) {
  const f = foldedOf(ch);
  out[ch] = { rotate: f.rotate, bbox: bboxOf(f.polygons), polys: f.polygons };
  fs.writeFileSync(path.join(os.tmpdir(), `_legacy_${ch}.svg`), svgOf(f.polygons));   // 仅目视核对
}
fs.writeFileSync(path.join(ROOT, 'tests', 'Origami.Tests', 'legacy_letters.json'), JSON.stringify(out, null, 1));

console.log('字符 rotate bbox');
for (const ch of letters) {
  const [x0, y0, x1, y1] = out[ch].bbox;
  console.log(`  ${ch}   ${out[ch].rotate}    ${(x1 - x0).toFixed(0)} x ${(y1 - y0).toFixed(0)}`);
}

function bboxOf(polys) {
  const pts = polys.flat();
  return [Math.min(...pts.map(p => p.x)), Math.min(...pts.map(p => p.y)),
          Math.max(...pts.map(p => p.x)), Math.max(...pts.map(p => p.y))];
}
