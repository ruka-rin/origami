// 跑旧实现的 showUnfoldedSquare（方形铺排），把绘制调用换成记录器，
// 导出「方形折痕线段」—— 这是旧实现对方形铺排的权威输出。
// 用法: node tools/_legacy-square.js
const fs = require('fs');
const path = require('path');

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

/** 截取 `name = { ... };` 对象字面量。 */
function grabObj(name) {
  const start = src.indexOf('\n  ' + name + ' = {');
  let i = src.indexOf('{', start), d = 0, j = i;
  for (; j < src.length; j++) {
    if (src[j] === '{') d++;
    else if (src[j] === '}' && --d === 0) break;
  }
  return src.slice(start, j + 1);
}

/** 截取 `name = <number>;`。 */
function grabNum(name) {
  const start = src.indexOf('\n  ' + name + ' = ');
  const end = src.indexOf(';', start);
  return src.slice(start, end + 1);
}

const code = [
  'var margin, outerStroke, paperFill, creaseStroke, gridStroke, fontLetters, autobox, showUnfoldedSquare;',
  grabNum('margin'),
  grabObj('outerStroke'),
  grab('paperFill'),
  grabObj('creaseStroke'),
  grabObj('gridStroke'),
  grab('fontLetters'),
  grab('autobox'),
  grab('showUnfoldedSquare'),
  'return { showUnfoldedSquare };',
].join('\n');

const legacy = new Function(code)();

/** 记录式 svg stub：链式 API 全靠返回自身。 */
function recorder() {
  const rec = [];
  const self = {
    _style: null,
    group() { return self; },
    translate() { return self; },
    rect() { return self; },
    addClass() { return self; },
    fill() { return self; },
    viewbox() { return self; },
    bbox() { return { x: 0, y: 0, width: 0, height: 0 }; },
    line(x1, y1, x2, y2) { rec.push({ x1, y1, x2, y2, style: null }); self._last = rec[rec.length - 1]; return self; },
    stroke(style) { if (self._last) { self._last.style = style; self._last = null; } return self; },
  };
  return { self, rec };
}

const data = JSON.parse(
  fs.readFileSync(path.join(root, 'tests', 'Origami.Tests', 'legacy_text.json'), 'utf-8'));

const words = ['A', 'L', 'V', '6', '68', '6S', '8S', '86', 'SOS', '68S', '68S8', 'KRENIO', 'G', '1', '5', '123456'];

const out = {};
for (const w of words) {
  const it = data[`${w}|left=0|mirror=0`];
  if (!it) { console.error('缺 ' + w); continue; }

  const letter = {
    creases: it.creases.map(c => ({ xb: c[0], xt: c[1] })),
    width: it.width,
    height: 2,
    yb: 2,
    yt: 0,
  };

  const { self, rec } = recorder();
  legacy.showUnfoldedSquare(self, letter);

  const isCrease = s => s && s.color === 'purple';
  const creases = rec.filter(r => isCrease(r.style)).map(r => [r.x1, r.y1, r.x2, r.y2]);
  const outside = rec.filter(r => r.style && r.style.color === 'black').map(r => [r.x1, r.y1, r.x2, r.y2]);
  const grid = rec.filter(r => r.style && r.style.color === '#dddddd').length;

  out[w] = { width: it.width, creases, outside, gridLines: grid };
  console.log(`${w.padEnd(8)} 折痕=${String(creases.length).padStart(3)} 边界=${String(outside.length).padStart(3)} 网格=${grid}`);
}

fs.writeFileSync(path.join(root, 'tests', 'Origami.Tests', 'legacy_square.json'),
                 JSON.stringify(out, null, 1));
console.log('\n-> tests/Origami.Tests/legacy_square.json');
