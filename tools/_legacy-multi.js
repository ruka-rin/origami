// 用旧实现（strip.js）的算法，从 legacy_text.json 的权威 creases 生成
// 多字符文本的成品 SVG —— 作为本项目朝向的黄金基准。
// 用法: node tools/_legacy-multi.js
const fs = require('fs');
const path = require('path');
const os = require('os');

const root = path.join(__dirname, '..');
const src = fs.readFileSync(path.join(root, '..', '旧的参考代码', 'strip.js'), 'utf-8');

/** 按花括号配对截取 `name = function (...) {...}` 整段。 */
function grab(name) {
  const start = src.indexOf('\n  ' + name + ' = function');
  if (start < 0) throw new Error('找不到 ' + name);
  let i = src.indexOf('{', start), d = 0, j = i;
  for (; j < src.length; j++) {
    if (src[j] === '{') d++;
    else if (src[j] === '}' && --d === 0) break;
  }
  return src.slice(start, j + 1);
}

/** fontEnc 是对象字面量，单独特判。 */
function grabObj(name) {
  const start = src.indexOf('\n  ' + name + ' = {');
  let i = src.indexOf('{', start), d = 0, j = i;
  for (; j < src.length; j++) {
    if (src[j] === '{') d++;
    else if (src[j] === '}' && --d === 0) break;
  }
  return src.slice(start, j + 1);
}

const code = [
  'var makePoint, reflectPoint, reflectPolygon, rotatePoint, rotatePolygon, foldStrip, parseEnc, fontEnc;',
  grabObj('fontEnc'),
  grab('parseEnc'),
  grab('makePoint'),
  grab('reflectPoint'),
  grab('reflectPolygon'),
  grab('rotatePoint'),
  grab('rotatePolygon'),
  grab('foldStrip'),
  'return { fontEnc, parseEnc, foldStrip };',
].join('\n');

const legacy = new Function(code)();

const data = JSON.parse(
  fs.readFileSync(path.join(root, 'tests', 'Origami.Tests', 'legacy_text.json'), 'utf-8'));

const words = ['V', 'L', 'A', 'SOS', 'SOSO', '68S', 'KRENIO', 'PROTEIN'];

const out = {};
for (const w of words) {
  const key = `${w}|left=0|mirror=0`;
  const it = data[key];
  if (!it) { console.error('缺 ' + key); continue; }

  // 复刻 parseEnc：从完整编码算出 creases 与 rotate（与 json 的 creases 核对）
  const parsed = legacy.parseEnc(it.encoding, false);
  if (JSON.stringify(parsed.creases) !== JSON.stringify(it.creases.map(c => ({ xb: c[0], xt: c[1] })))) {
    console.error(`${w}: creases 与 json 不一致`);
  }

  const letter = { creases: parsed.creases, yb: 2, yt: 0, rotate: parsed.rotate };
  const polys = legacy.foldStrip(letter);
  out[w] = { rotate: parsed.rotate, polys };

  // 归一化 -> SVG（旧实现直接输出 y 向下，不做 flipY）
  const pts = polys.flat();
  const x0 = Math.min(...pts.map(p => p.x)), x1 = Math.max(...pts.map(p => p.x));
  const y0 = Math.min(...pts.map(p => p.y)), y1 = Math.max(...pts.map(p => p.y));
  const u = 10, pad = 4;
  const W = (x1 - x0) * u + 2 * pad, H = (y1 - y0) * u + 2 * pad;
  const Sx = p => pad + (p.x - x0) * u;
  const Sy = p => pad + (p.y - y0) * u;

  const hues = ['#5577cc', '#cc4444'];
  let body = '';
  let k = 0;
  for (const poly of polys) {
    const ptsStr = poly.map(p => `${Sx(p).toFixed(1)},${Sy(p).toFixed(1)}`).join(' ');
    body += `<polygon points="${ptsStr}" fill="none" stroke="${hues[k % 2]}" stroke-width="0.6"/>`;
    k++;
  }
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}">` +
    `<rect width="100%" height="100%" fill="#fff"/>${body}</svg>`;

  // 仅用于人工目视核对，不入库
  fs.writeFileSync(path.join(os.tmpdir(), `_legacy_multi_${w.replace(/ /g, '_')}.svg`), svg);
  console.log(`${w.padEnd(12)} rotate=${parsed.rotate} 面片=${polys.length}`);
}

// 直接写进测试目录，作为形状核对的基准数据
fs.writeFileSync(
  path.join(root, 'tests', 'Origami.Tests', 'legacy_multi.json'),
  JSON.stringify(out));
