// Run with: node --test packages/design-tokens
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  TOKENS_FILE, OUTPUTS, TokenError, parseTokens, render, renderCss, renderUss, renderCs,
  missingTokens, checkOutputs, ussSupports, csharpIdent, cssName,
} from './build.mjs';

const realTree = JSON.parse(readFileSync(TOKENS_FILE, 'utf8'));
const tok = (type, value) => ({ $type: type, $value: value });
const errorsOf = (tree) => {
  try {
    parseTokens(tree);
  } catch (e) {
    assert.ok(e instanceof TokenError);
    return e.errors.join('\n');
  }
  assert.fail('expected parseTokens to throw');
};

test('tokens.json parses and covers DESIGN_SYSTEM.md sections 2 to 5', () => {
  const ids = parseTokens(realTree).map((t) => t.path.join('.'));
  for (const id of ['color.bg.base', 'color.brand.volt-ink', 'color.category.brain', 'color.rank.legend',
    'font.family.display', 'font.size.hero-number', 'font.letter-spacing.label', 'space.72',
    'layout.gutter.desktop', 'radius.pill', 'border.width', 'motion.slow.spring-damping',
    'motion.celebrate.duration-max', 'motion.press-scale']) {
    assert.ok(ids.includes(id), id);
  }
  assert.equal(ids.filter((id) => id.startsWith('color.category.')).length, 7);
  assert.equal(ids.filter((id) => id.startsWith('color.rank.')).length, 6);
  assert.equal(ids.filter((id) => id.startsWith('space.')).length, 10);
});

test('every output generated from tokens.json declares every token it must', () => {
  const tokens = parseTokens(realTree);
  for (const o of render(tokens)) {
    assert.deepEqual(missingTokens(tokens, o.kind, o.content), [], o.path);
  }
});

test('CSS and C# carry every token; USS lists each token it cannot express', () => {
  const tokens = parseTokens(realTree);
  const uss = renderUss(tokens);
  for (const t of tokens.filter((x) => !ussSupports(x))) {
    assert.ok(uss.includes('   ' + t.path.join('.') + '\n'), t.path.join('.'));
  }
  assert.equal(missingTokens(tokens, 'css', renderCss(tokens)).length, 0);
  assert.equal(missingTokens(tokens, 'cs', renderCs(tokens)).length, 0);
});

test('validation rejects bad values, types and keys', () => {
  assert.match(errorsOf({ a: tok('color', '#12345') }), /a: invalid color/);
  assert.match(errorsOf({ a: tok('colour', '#123456') }), /unknown \$type "colour"/);
  assert.match(errorsOf({ a: tok('dimension', '16') }), /invalid dimension/);
  assert.match(errorsOf({ a: tok('duration', '0.2s') }), /invalid duration/);
  assert.match(errorsOf({ a: tok('cubicBezier', [1.5, 0, 0, 1]) }), /invalid cubicBezier/);
  assert.match(errorsOf({ a: tok('cubicBezier', 'bouncy') }), /invalid cubicBezier/);
  assert.match(errorsOf({ a: tok('fontWeight', 650) }), /invalid fontWeight/);
  assert.match(errorsOf({ a: tok('string', 'a;b') }), /invalid string/);
  assert.match(errorsOf({ a: tok('fontFamily', ['Bad"Name']) }), /invalid fontFamily/);
  assert.match(errorsOf({ bgBase: tok('color', '#123456') }), /kebab-case/);
  assert.match(errorsOf({ a: { ...tok('color', '#123456'), $extra: 1 } }), /unexpected key/);
});

test('validation rejects names that collide in CSS or C#', () => {
  assert.match(errorsOf({ x: { 'a-b': tok('number', 1) }, 'x-a': { b: tok('number', 2) } }), /both become --gv-x-a-b/);
  assert.match(errorsOf({ x: { 'a-1': tok('number', 1), a1: tok('number', 2) } }), /C# name GameVerseTokens.X.A1 collides/);
  assert.match(errorsOf({ x: { x: tok('number', 1) } }), /same name as its enclosing class/);
});

test('value formatting per output', () => {
  const tokens = parseTokens({
    c: tok('color', '#FF000080'),
    e: { k: tok('cubicBezier', 'ease-out'), a: tok('cubicBezier', [0.2, 0.8, 0.2, 1]) },
    d: tok('duration', '160ms'),
    f: tok('fontFamily', ['Satoshi', 'Inter']),
    s: { 16: tok('dimension', '16px'), em: tok('dimension', '-0.02em') },
  });
  const css = renderCss(tokens);
  assert.match(css, /--gv-c: #FF000080;/);
  assert.match(css, /--gv-e-k: ease-out;/);
  assert.match(css, /--gv-e-a: cubic-bezier\(0.2, 0.8, 0.2, 1\);/);
  assert.match(css, /--gv-f: "Satoshi", "Inter", sans-serif;/);

  const uss = renderUss(tokens);
  assert.match(uss, /--gv-e-k: ease-out;/);
  assert.doesNotMatch(uss, /--gv-e-a:/);
  assert.doesNotMatch(uss, /--gv-s-em:/);
  assert.match(uss, /--gv-s-16: 16px;/);

  const cs = renderCs(tokens);
  assert.match(cs, /Color32 C = new Color32\(255, 0, 0, 128\);/);
  assert.match(cs, /CubicBezier K = new CubicBezier\(0.0f, 0.0f, 0.58f, 1.0f\);/);
  assert.match(cs, /const float D = 0.16f;/);
  assert.match(cs, /new\[\] \{ "Satoshi", "Inter" \}/);
  assert.match(cs, /const float _16 = 16.0f;/);
  assert.match(cs, /const float Em = -0.02f;/);
});

test('naming helpers', () => {
  assert.equal(csharpIdent('volt-ink'), 'VoltInk');
  assert.equal(csharpIdent('16'), '_16');
  assert.equal(cssName({ path: ['color', 'brand', 'volt-ink'] }), '--gv-color-brand-volt-ink');
});

test('check reports a hand-deleted token and stale or missing files', () => {
  const tokens = parseTokens(realTree);
  const files = Object.fromEntries(render(tokens).map((o) => [o.path, o.content]));
  assert.deepEqual(checkOutputs(tokens, (p) => files[p]), []);

  const css = OUTPUTS[0].path;
  const broken = { ...files, [css]: files[css].replace(/^ {2}--gv-color-brand-volt: .*\n/m, '') };
  delete broken[OUTPUTS[3].path];
  const problems = checkOutputs(tokens, (p) => broken[p]);
  assert.ok(problems.includes(`${css}: token color.brand.volt is missing`), problems.join('\n'));
  assert.ok(problems.includes(`${css}: out of date with tokens.json`));
  assert.ok(problems.includes(`${OUTPUTS[3].path}: missing`));

  const crlf = { ...files, [css]: files[css].replace(/\n/g, '\r\n') };
  assert.deepEqual(checkOutputs(tokens, (p) => crlf[p]), [], 'CRLF checkouts are not stale');
});
