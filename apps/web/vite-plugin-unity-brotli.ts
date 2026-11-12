import { createReadStream, existsSync, statSync } from 'node:fs';
import { join, normalize, resolve } from 'node:path';
import type { Connect, Plugin } from 'vite';

/**
 * Serves Unity Web build files under /unity/ that end in .br with `Content-Encoding: br` and the type of the file inside,
 * so the browser decompresses them natively. Unity builds use Brotli without its JS decompression fallback (smaller and
 * faster), which only works when the server sends this header. Production (Traefik/nginx/Cloudflare) needs the same rule;
 * ADR-0002 records it. Browsers accept `br` only over HTTPS or localhost.
 */
const TYPES: Record<string, string> = {
  '.js': 'application/javascript',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
  '.json': 'application/json',
  '.symbols.json': 'application/json',
};

function serveBrotli(root: () => string): Connect.NextHandleFunction {
  return (req, res, next) => {
    const url = decodeURIComponent((req.url ?? '').split('?')[0] ?? '');
    if (!url.startsWith('/unity/') || !url.endsWith('.br')) return next();

    const dir = root();
    const file = normalize(join(dir, url));
    if (!file.startsWith(dir) || !existsSync(file)) return next();

    const inner = url.slice(0, -'.br'.length);
    const ext = inner.slice(inner.lastIndexOf('.'));
    res.setHeader('Content-Encoding', 'br');
    res.setHeader('Content-Type', TYPES[ext] ?? 'application/octet-stream');
    res.setHeader('Content-Length', statSync(file).size);
    // Measured through a Cloudflare quick tunnel: the edge re-encodes this Brotli body as gzip (6.2 MB wasm -> 8.7 MB on
    // the wire), and with `no-transform` it sends it uncompressed (23.9 MB). Brotli pass-through must be configured and
    // verified on the production Cloudflare zone (ADR-0002).
    res.setHeader('Cache-Control', 'public, max-age=3600');
    res.setHeader('Vary', 'Accept-Encoding');
    createReadStream(file).pipe(res);
  };
}

export function unityBrotli(): Plugin {
  let publicDir = '';
  let outDir = '';
  return {
    name: 'gameverse-unity-brotli',
    configResolved(config) {
      publicDir = resolve(config.publicDir);
      outDir = resolve(config.root, config.build.outDir);
    },
    configureServer(server) {
      server.middlewares.use(serveBrotli(() => publicDir));
    },
    configurePreviewServer(server) {
      server.middlewares.use(serveBrotli(() => outDir));
    },
  };
}
