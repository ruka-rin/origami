/*
从旧参考实现（旧的参考代码/strip.js）导出字库与折痕，供一致性核对测试使用。

用法（在项目根目录执行）：
    node tools/extract-legacy-font.js

产物：
    tests/Origami.Tests/legacy_font.json

为什么需要它：旧实现是 JavaScript，字库编码串里满是反斜杠转义
（JS 的 `\\` 与 C# 的 `\\` 含义相同，但**手抄必然出错**）。
这个脚本让 JS 引擎自己求值，再把结果落成 JSON ——
测试比对的是**两边的运行时真实值**，不含任何手工转录。

★ 关键教训（第一版踩过）：**不要对源码做「去注释」之类的文本变换**。
   编码里的 `\\` 会被行尾注释剥离正则吃掉后半段，导出的字符串少一个
   反斜杠，看起来像本项目的 bug，实则是导出工具的错——
   白白排查了很久。正确做法：只截取字面量区间，交给 JS 引擎求值。
*/
'use strict';
const fs = require('fs');
const path = require('path');

// 允许从任意位置运行
const ROOT = path.resolve(__dirname, '..');
const LEGACY = path.resolve(ROOT, '..', '旧的参考代码', 'strip.js');
const OUT = path.join(ROOT, 'tests', 'Origami.Tests', 'legacy_font.json');

if (!fs.existsSync(LEGACY)) {
  console.error(`找不到旧参考代码：${LEGACY}`);
  process.exit(1);
}

const src = fs.readFileSync(LEGACY, 'utf-8');

/**
 * 从 startFrom处扫描一个 {...} 区间，正确跳过字符串字面量。
 * @returns {[number, number]} [起始, 结束) 下标
 */
function scanBracket(s, startFrom, open, close) {
  let depth = 0, inStr = null;
  for (let i = startFrom; i < s.length; i++) {
    const c = s[i];
    if (inStr) {
      if (c === '\\') { i++; continue; }        // 转义：跳过下一个字符
      if (c === inStr) inStr = null;
      continue;
    }
    if (c === '"' || c === "'") { inStr = c; continue; }
    if (c === open) depth++;
    else if (c === close) { depth--; if (depth === 0) return [startFrom, i + 1]; }
  }
  throw new Error('括号未配对');
}

// ---- fontEnc 对象字面量 ----
const fontStart = src.indexOf('fontEnc = {');
if (fontStart < 0) throw new Error('未找到 fontEnc');
const [fontBegin, fontEnd] = scanBracket(src, src.indexOf('{', fontStart), '{', '}');
const fontEnc = eval('(' + src.slice(fontBegin, fontEnd) + ')');

// ---- parseEnc 函数（只截取区间，内部原文不动）----
const pStart = src.indexOf('parseEnc = function');
if (pStart < 0) throw new Error('未找到 parseEnc');
const fnKeyword = src.indexOf('function', pStart);
const [, pEnd] = scanBracket(src, src.indexOf('{', pStart), '{', '}');
const parseEnc = eval('(' + ('function' + src.slice(fnKeyword + 8, pEnd)) + ')');

const out = {};
for (const key of Object.keys(fontEnc)) {
  const r = parseEnc(fontEnc[key], false);
  out[key] = {
    encoding: fontEnc[key],
    width: r.width,
    rotate: r.rotate,
    creases: r.creases.map(c => [c.xb, c.xt]),
  };
}

fs.writeFileSync(OUT, JSON.stringify(out, null, 1), 'utf-8');
console.log(`导出 ${Object.keys(out).length} 条 -> ${path.relative(ROOT, OUT)}`);
for (const k of ['6', '8', 'S']) {
  console.log(`  ${k}: ${JSON.stringify(fontEnc[k])}`);
}
