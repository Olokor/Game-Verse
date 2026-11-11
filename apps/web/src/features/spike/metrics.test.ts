import { describe, expect, it } from 'vitest';
import { downloadStats, fpsStats, formatResult, unityFps } from './metrics';

describe('fpsStats', () => {
  it('reports a steady 60 fps', () => {
    const t = Array.from({ length: 601 }, (_, i) => i * (1000 / 60));
    const s = fpsStats(t);
    expect(s.frames).toBe(600);
    expect(s.average).toBe(60);
    expect(s.low1).toBe(60);
    expect(s.longFrames).toBe(0);
  });

  it('exposes stutter in the 1% low and hitch count while the average stays high', () => {
    const t = [0];
    // 4 of 200 frames (2%) take 100 ms: the 99th-percentile frame is one of them.
    for (let i = 1; i <= 200; i++) t.push((t[i - 1] ?? 0) + (i % 50 === 0 ? 100 : 1000 / 60));
    const s = fpsStats(t);
    expect(s.average).toBeGreaterThan(50);
    expect(s.low1).toBe(10);
    expect(s.longFrames).toBe(4);
  });

  it('handles too few samples', () => {
    expect(fpsStats([5])).toEqual({ average: 0, low1: 0, longFrames: 0, frames: 0 });
  });
});

describe('downloadStats', () => {
  it('sums only the files of the requested build', () => {
    const s = downloadStats(
      [
        { name: 'https://x/unity/spike-2d/Build/spike-2d.wasm.br', transferSize: 300, encodedBodySize: 290, responseEnd: 900 },
        { name: 'https://x/unity/spike-2d/Build/spike-2d.data.br', transferSize: 0, encodedBodySize: 100, responseEnd: 1200.4 },
        { name: 'https://x/unity/spike-3d/Build/spike-3d.wasm.br', transferSize: 999, encodedBodySize: 999, responseEnd: 5000 },
        { name: 'https://x/assets/index.js', transferSize: 50, encodedBodySize: 50, responseEnd: 100 },
      ],
      '/unity/spike-2d/Build/',
    );
    expect(s).toEqual({ transferBytes: 300, bodyBytes: 390, files: 2, finishedAt: 1200 });
  });
});

describe('formatResult', () => {
  it('prints sizes in MB and times in seconds', () => {
    const text = formatResult({
      scene: '2d',
      shellReady: 300,
      download: { transferBytes: 0, bodyBytes: 5 * 1024 * 1024, files: 4, finishedAt: 2500 },
      loadedAt: 4000,
      startup: 1500,
      fps: { average: 59.8, low1: 41.2, longFrames: 1, frames: 600 },
      unityFps: 30,
      devicePixelRatio: 2,
      canvas: '720x1280',
      gpu: 'Mali-G72',
      userAgent: 'test',
    });
    expect(text).toContain('build download: 5.00 MB (4 files; on the wire this load: 0.00 MB)');
    expect(text).toContain('startup (CPU, after download): 1.50 s');
    expect(text).toContain('unity fps: 30');
    expect(text).toContain('browser fps over 600 frames: avg 59.8, 1% low 41.2');
  });
});

describe('unityFps', () => {
  it('divides rendered frames by elapsed time', () => {
    expect(unityFps(100, 400, 10000)).toBe(30);
  });

  it('is undefined when Unity did not report frames', () => {
    expect(unityFps(undefined, 400, 10000)).toBeUndefined();
    expect(unityFps(1, 2, 0)).toBeUndefined();
  });
});
