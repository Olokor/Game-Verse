import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * CLAUDE.md: "Never hardcode a color, font size, radius or duration in components."
 * Scans app source (not the generated token files) for raw design values. Fix a failure by using a token;
 * if the token doesn't exist, add it to packages/design-tokens/tokens.json and run `make tokens`.
 */
const SRC = join(__dirname, '..');
const GENERATED = new Set(['styles/tokens.css', 'styles/theme.css']);

const RULES: Array<{ name: string; pattern: RegExp }> = [
  { name: 'hex color', pattern: /#[0-9a-fA-F]{3,8}\b(?![-\w])/g },
  { name: 'rgb/hsl/oklch color', pattern: /\b(?:rgba?|hsla?|oklch|oklab|lab|lch)\(/g },
  { name: 'named CSS color', pattern: /\b(?:color|background(?:-color)?|fill|stroke|border-color)\s*:\s*(?:white|black|red|green|blue|gray|grey)\b/g },
  { name: 'Tailwind arbitrary color', pattern: /\b(?:bg|text|border|ring|fill|stroke|from|to|via|shadow|outline|decoration)-\[(?:#|rgb|hsl|oklch)/g },
  { name: 'Tailwind arbitrary px/rem size', pattern: /\b[a-z-]+-\[-?\d+(?:\.\d+)?(?:px|rem|em)\]/g },
  { name: 'raw duration', pattern: /\b\d+(?:\.\d+)?m?s\b(?=[\s;,)'"`])/g },
  { name: 'Tailwind default palette', pattern: /\b(?:bg|text|border|ring|fill|stroke)-(?:slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}\b/g },
];

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) return files(full);
    return /\.(tsx?|css)$/.test(name) && !/\.test\.tsx?$/.test(name) ? [full] : [];
  });
}

/** Comments may mention values (e.g. documenting a token), so strip them before scanning. */
const stripComments = (text: string) => text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');

describe('design values come from tokens', () => {
  const sources = files(SRC)
    .map((path) => ({ path, rel: relative(SRC, path).replace(/\\/g, '/') }))
    .filter(({ rel }) => !GENERATED.has(rel));

  it('scans the app source', () => {
    expect(sources.length).toBeGreaterThan(20);
  });

  for (const { name, pattern } of RULES) {
    it(`has no ${name}`, () => {
      const hits: string[] = [];
      for (const { path, rel } of sources) {
        stripComments(readFileSync(path, 'utf8'))
          .split('\n')
          .forEach((line, i) => {
            for (const m of line.matchAll(pattern)) hits.push(`${rel}:${i + 1}  ${m[0]}`);
          });
      }
      expect(hits).toEqual([]);
    });
  }
});
