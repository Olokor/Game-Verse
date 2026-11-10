#!/usr/bin/env node
// Prompt 0.7 spike (throwaway, removed with ADR-0002): prints the download size of each Unity Web build under <root>.
// For every build folder: the Brotli files (what players download) and their decompressed size (what the browser holds).
// Usage: node web-spike-sizes.mjs apps/web/public/unity
import { readdirSync, readFileSync, statSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { brotliDecompressSync } from 'node:zlib';

const root = process.argv[2];
if (!root || !existsSync(root)) {
  console.error(`usage: web-spike-sizes.mjs <folder with Unity Web builds> (not found: ${root})`);
  process.exit(2);
}

const mb = (bytes) => (bytes / 1024 / 1024).toFixed(2);
const report = {};
for (const build of readdirSync(root).filter((d) => existsSync(join(root, d, 'Build')))) {
  const dir = join(root, build, 'Build');
  const files = readdirSync(dir).map((name) => {
    const path = join(dir, name);
    const bytes = statSync(path).size;
    const raw = name.endsWith('.br') ? brotliDecompressSync(readFileSync(path)).length : bytes;
    return { name, bytes, raw };
  });
  const total = files.reduce((sum, f) => sum + f.bytes, 0);
  const totalRaw = files.reduce((sum, f) => sum + f.raw, 0);
  report[build] = { downloadBytes: total, rawBytes: totalRaw, files };

  console.log(`\n${build}: ${mb(total)} MB download (Brotli), ${mb(totalRaw)} MB decompressed`);
  for (const f of files.sort((a, b) => b.bytes - a.bytes)) {
    console.log(`  ${f.name.padEnd(36)} ${mb(f.bytes).padStart(7)} MB   raw ${mb(f.raw).padStart(7)} MB`);
  }
}

writeFileSync(join(root, 'sizes.json'), JSON.stringify(report, null, 2));
