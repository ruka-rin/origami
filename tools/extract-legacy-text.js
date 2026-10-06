/*
从旧参考实现导出「多字符文本 -> 编码串」的结果，供文本级一致性核对。

用法：
    node tools/extract-legacy-text.js

产物：
    tests/Origami.Tests/legacy_text.json

与 extract-legacy-font.js 的区别：那个比单字模（编码串层面），
这个比**多字符文本的完整编码流程** —— 逐行拼接、奇偶行反转/镜像、
行间转折符、left 模式的前置竖折痕，这些逻辑只在多字符时才走通，
单字模根本碰不到。
*/
'use strict';
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const LEGACY = path.resolve(ROOT, '..', '旧的参考代码', 'strip.js');
const OUT = path.join(ROOT, 'tests', 'Origami.Tests', 'legacy_text.json');

const src = fs.readFileSync(LEGACY, 'utf-8');

/** 扫描 {...} 区间，正确跳过字符串字面量。 */
function scanBracket(s, startFrom, open, close) {
  let depth = 0, inStr = null;
  for (let i = startFrom; i < s.length; i++) {
    const c = s[i];
    if (inStr) {
      if (c === '\\') { i++; continue; }
      if (c === inStr) inStr = null;
      continue;
    }
    if (c === '"' || c === "'") { inStr = c; continue; }
    if (c === open) depth++;
    else if (c === close) { depth--; if (depth === 0) return [startFrom, i + 1]; }
  }
  throw new Error('括号未配对');
}

const fontStart = src.indexOf('fontEnc = {');
const [fb, fe] = scanBracket(src, src.indexOf('{', fontStart), '{', '}');
const fontEnc = eval('(' + src.slice(fb, fe) + ')');

const pStart = src.indexOf('parseEnc = function');
const fnKeyword = src.indexOf('function', pStart);
const [, pe] = scanBracket(src, src.indexOf('{', pStart), '{', '}');
const parseEnc = eval('(' + ('function' + src.slice(fnKeyword + 8, pe)) + ')');

/**
 * 复刻 index.js 里 update() 的编码流程。
 * @param {string} text
 * @param {{left?:boolean, mirror?:boolean}} state
 * @returns {string} 编码串
 */
function encodeText(text, state) {
  const left = !!state.left;
  const mirror = !!state.mirror;

  let lines = text.split('\n');
  while (lines.length > 0 && lines[0].length === 0) lines.shift();
  while (lines.length > 0 && lines[lines.length - 1].length === 0) lines.pop();

  const parity = Number(left);
  const out = [];

  for (let row = 0; row < lines.length; row++) {
    const line = lines[row];
    const parts = [];

    for (let col = 0; col < line.length; col++) {
      let ch = line[col].toUpperCase();
      if (ch in fontEnc) {
        if (row === col && col === 0 && (ch + 'start') in fontEnc && !left) {
          ch = ch + 'start';
        }
        // ★ 旧实现用 replace(/^[ _]/, '') 只剥一个前导字符
        parts.push(fontEnc[ch].replace(/^[ _]/, ''));
      } else {
        continue;
      }
    }

    let lineEnc = parts.join('');

    if (row % 2 === 1 - parity) {
      lineEnc = mirror ? `#_${lineEnc}#`
                       : lineEnc.split('').reverse().join('');
    }

    if (row < lines.length - 1) {
      lineEnc += (row % 2 === parity) ? '\\_____/' : '_/_____\\_';
    }
    out.push(lineEnc);
  }

  // ★ 同时保留「left 变换前」的原始拼接 —— 定位差异出在行拼接还是 left 分支
  const raw = out.join('');

  let enc = raw;

  if (left) {
    let index = enc.indexOf('~');
    // 旧代码没有边界检查 —— 找不到 ~ 时 indexOf 返回 -1，
    // 此处显式处理以免循环失控（见下方 notes）
    if (index < 0) return { enc: raw, note: 'no-tilde', raw };
    while (enc[index + 1] === '@') index++;
    enc = `${enc.slice(0, +index + 1 || 9e9)}${mirror ? '' : '#'}|${enc.slice(index + 1)}`;
  }

  return { enc, raw };
}

// 测试语料：覆盖单行/多行、奇偶行、含空格、各种字符组合
const TEXTS = [
  'A', 'B', 'AB', 'KRENIO', 'PROTEIN', '123456', 'HELLO WORLD',
  'ABC', '1234', 'XYZ', '6', '8', 'S', '68S', 'OO', 'MM',
  'A\nB', 'AB\nCD', 'A\nB\nC', 'ABC\nDEF', 'A B C',
  'THE QUICK BROWN', 'A\n\nB', '  A  ', '1234567890',
  'Z\nY\nX', 'QWERTY', 'ASDFGH', '0000', '8888',
  // 用户实测出现过朝向问题的词，加进基准
  'SOS', 'V', 'L', 'X', 'M', 'SOSO',
];

const results = {};
for (const text of TEXTS) {
  for (const left of [false, true]) {
    for (const mirror of [false, true]) {
      const key = `${text}|left=${left ? 1 : 0}|mirror=${mirror ? 1 : 0}`;
      const r = encodeText(text, { left, mirror });
      let width = null, creases = null, note = r.note || null;
      if (r.enc !== null && r.enc !== undefined) {
        const p = parseEnc(r.enc, false);
        width = p.width;
        creases = p.creases.map(c => [c.xb, c.xt]);
      }
      results[key] = { encoding: r.enc, raw: r.raw, width, creases, note };
    }
  }
}

fs.writeFileSync(OUT, JSON.stringify(results, null, 1), 'utf-8');
const n = Object.keys(results).length;
console.log(`导出 ${n} 组（${TEXTS.length} 段文本 x left/mirror 四种组合）`);
console.log(`-> ${path.relative(ROOT, OUT)}`);

// 挑一个打印出来核对
const sample = results['KRENIO|left=0|mirror=0'];
console.log(`\nKRENIO (left=0, mirror=0):`);
console.log(`  width=${sample.width}  creases=${sample.creases.length}`);
console.log(`  编码前60: ${sample.encoding.slice(0, 60)}`);
