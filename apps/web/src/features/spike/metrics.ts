/**
 * Prompt 0.7 web performance spike (throwaway, deleted with ADR-0002): the numbers the /spike page reports.
 * Pure functions so they are unit-tested; the page only collects timestamps and resource entries.
 */

export interface FpsStats {
  /** Mean frames per second over the sample. */
  average: number;
  /** "1% low": the frame rate implied by the 99th-percentile frame time (stutter indicator). */
  low1: number;
  /** Frames longer than 50 ms (visible hitches). */
  longFrames: number;
  frames: number;
}

/** Frame stats from consecutive requestAnimationFrame timestamps (ms). Unity's main loop runs on rAF. */
export function fpsStats(timestamps: readonly number[]): FpsStats {
  const deltas: number[] = [];
  for (let i = 1; i < timestamps.length; i++) deltas.push((timestamps[i] ?? 0) - (timestamps[i - 1] ?? 0));
  if (deltas.length === 0) return { average: 0, low1: 0, longFrames: 0, frames: 0 };

  const total = deltas.reduce((sum, d) => sum + d, 0);
  const sorted = [...deltas].sort((a, b) => a - b);
  const p99 = sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * 0.99) - 1)] ?? total;
  return {
    average: round1((deltas.length * 1000) / total),
    low1: round1(1000 / p99),
    longFrames: deltas.filter((d) => d > 50).length,
    frames: deltas.length,
  };
}

export interface DownloadStats {
  /** Bytes on the wire (0 for files served from the HTTP cache). */
  transferBytes: number;
  /** Compressed body bytes, cached or not: the real download size of the build. */
  bodyBytes: number;
  files: number;
  /** ms since navigation start when the last build file finished downloading. */
  finishedAt: number;
}

type ResourceEntry = Pick<PerformanceResourceTiming, 'name' | 'transferSize' | 'encodedBodySize' | 'responseEnd'>;

/** Sums the Resource Timing entries that belong to one Unity build folder (e.g. "/unity/spike-2d/Build/"). */
export function downloadStats(entries: readonly ResourceEntry[], buildPath: string): DownloadStats {
  const mine = entries.filter((e) => e.name.includes(buildPath));
  return {
    transferBytes: mine.reduce((sum, e) => sum + e.transferSize, 0),
    bodyBytes: mine.reduce((sum, e) => sum + e.encodedBodySize, 0),
    files: mine.length,
    finishedAt: Math.round(mine.reduce((max, e) => Math.max(max, e.responseEnd), 0)),
  };
}

export interface SpikeResult {
  scene: string;
  /** Page shell ready (React mounted), ms since navigation start. */
  shellReady: number;
  download: DownloadStats;
  /** Unity instance created, ms since navigation start: the player can see and use the game. */
  loadedAt: number;
  /** loadedAt minus download.finishedAt: decompression, wasm compile and engine start, i.e. device CPU, not network. */
  startup: number;
  /** Browser frame stats (requestAnimationFrame): the display rate, a ceiling for the game. */
  fps?: FpsStats;
  /** Frames Unity actually rendered per second over the same window (window.__unityFrames); undefined if not reported. */
  unityFps?: number;
  devicePixelRatio: number;
  canvas: string;
  /** WebGL renderer string, e.g. "Apple GPU" or "Mali-G52"; "SwiftShader" means software rendering (FPS not meaningful). */
  gpu: string;
  userAgent: string;
}

export function formatResult(r: SpikeResult): string {
  const mb = (b: number) => (b / 1024 / 1024).toFixed(2);
  const s = (ms: number) => (ms / 1000).toFixed(2);
  return [
    `scene: ${r.scene}`,
    `build download: ${mb(r.download.bodyBytes)} MB (${r.download.files} files; on the wire this load: ${mb(r.download.transferBytes)} MB)`,
    `downloads finished: ${s(r.download.finishedAt)} s`,
    `startup (CPU, after download): ${s(r.startup)} s`,
    `playable: ${s(r.loadedAt)} s after navigation`,
    r.fps ? `unity fps: ${r.unityFps ?? 'not reported'}` : 'fps: measuring...',
    ...(r.fps
      ? [`browser fps over ${r.fps.frames} frames: avg ${r.fps.average}, 1% low ${r.fps.low1}, hitches >50ms: ${r.fps.longFrames}`]
      : []),
    `canvas: ${r.canvas} at dpr ${r.devicePixelRatio}`,
    `gpu: ${r.gpu}`,
    `ua: ${r.userAgent}`,
  ].join('\n');
}

/** The GPU name the browser exposes (falls back to the masked renderer string). */
export function gpuName(): string {
  try {
    const gl = document.createElement('canvas').getContext('webgl2');
    if (!gl) return 'no WebGL 2';
    const info = gl.getExtension('WEBGL_debug_renderer_info');
    return String(gl.getParameter(info ? info.UNMASKED_RENDERER_WEBGL : gl.RENDERER));
  } catch {
    return 'unknown';
  }
}

/** How long loading may sit without progress before the page treats the download as failed. */
export const STALL_MS = 45000;

/**
 * True when loading has made no progress for `limitMs`. Unity's loader reports progress as bytes arrive, so a long
 * silence means the connection dropped mid-download (the loader itself never rejects in that case).
 */
export function isStalled(lastProgressAt: number, now: number, limitMs: number = STALL_MS): boolean {
  return now - lastProgressAt >= limitMs;
}

/** Unity frames per second from two readings of its frame counter. */
export function unityFps(startFrame: number | undefined, endFrame: number | undefined, elapsedMs: number): number | undefined {
  if (startFrame === undefined || endFrame === undefined || elapsedMs <= 0) return undefined;
  return round1(((endFrame - startFrame) * 1000) / elapsedMs);
}

const round1 = (n: number) => Math.round(n * 10) / 10;
