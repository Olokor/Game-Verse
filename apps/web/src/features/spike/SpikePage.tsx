import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { Unity, useUnityContext } from 'react-unity-webgl';
import { Button, buttonClasses } from '../../components/ui/Button';
import { cn } from '../../lib/cn';
import { downloadStats, fpsStats, formatResult, gpuName, unityFps, type SpikeResult } from './metrics';

/**
 * /spike: Prompt 0.7 web performance spike (throwaway, deleted with ADR-0002). Only registered when the app is built
 * with VITE_SPIKE=true. Loads one Unity Web build per page load (switching scenes reloads the page, so every
 * measurement is a clean cold or warm load) and reports size, download, startup and frame rate.
 */
const SCENES = {
  '2d': { build: 'spike-2d', label: '2D: 200 sprites' },
  '3d': { build: 'spike-3d', label: '3D: car, baked light' },
  'lean-2d': { build: 'lean-2d', label: 'Lean 2D' },
  'lean-3d': { build: 'lean-3d', label: 'Lean 3D' },
} as const;
type SceneId = keyof typeof SCENES;

/** Canvas resolution cap. Phones report dpr 3 to 4; a shipped game would cap it, so the spike measures at 2. */
const MAX_DPR = 2;
const WARMUP_MS = 2000;
const SAMPLE_MS = 10000;

declare global {
  interface Window {
    /** Read by apps/web/scripts/spike-measure.mjs. */
    __spikeResult?: SpikeResult;
    /** Unity's Time.frameCount, set every frame by the spike's SpikeFrames.jslib. */
    __unityFrames?: number;
  }
}

export function SpikePage() {
  const [params] = useSearchParams();
  const requested = params.get('scene') ?? '2d';
  const sceneId: SceneId = requested in SCENES ? (requested as SceneId) : '2d';
  return <SpikeRun key={sceneId} sceneId={sceneId} />;
}

function SpikeRun({ sceneId }: { sceneId: SceneId }) {
  const { build, label } = SCENES[sceneId];
  const base = `/unity/${build}/Build/${build}`;
  const { unityProvider, loadingProgression, isLoaded, initialisationError } = useUnityContext({
    loaderUrl: `${base}.loader.js`,
    dataUrl: `${base}.data.br`,
    frameworkUrl: `${base}.framework.js.br`,
    codeUrl: `${base}.wasm.br`,
    companyName: 'GameVerse',
    productName: 'Web spike',
    productVersion: '0.7.0',
  });

  const shellReady = useRef(performance.now());
  const canvas = useRef<HTMLCanvasElement | null>(null);
  const dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR);
  const [result, setResult] = useState<SpikeResult>();
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!isLoaded) return;
    const loadedAt = performance.now();
    const download = downloadStats(performance.getEntriesByType('resource') as PerformanceResourceTiming[], `/unity/${build}/Build/`);
    const el = canvas.current;
    const base: SpikeResult = {
      scene: sceneId,
      shellReady: Math.round(shellReady.current),
      download,
      loadedAt: Math.round(loadedAt),
      startup: Math.max(0, Math.round(loadedAt - download.finishedAt)),
      devicePixelRatio: dpr,
      canvas: el ? `${el.width}x${el.height}` : 'unknown',
      gpu: gpuName(),
      userAgent: navigator.userAgent,
    };
    setResult(base);

    // Frame rate after a short warm-up (shader compile, first allocations), from rAF timestamps.
    const stamps: number[] = [];
    let startFrames: number | undefined;
    let frame = 0;
    const tick = (t: number) => {
      if (t - loadedAt >= WARMUP_MS) {
        if (stamps.length === 0) startFrames = window.__unityFrames;
        stamps.push(t);
      }
      if (t - loadedAt < WARMUP_MS + SAMPLE_MS) {
        frame = requestAnimationFrame(tick);
      } else {
        const elapsed = (stamps[stamps.length - 1] ?? 0) - (stamps[0] ?? 0);
        const done: SpikeResult = {
          ...base,
          canvas: el ? `${el.width}x${el.height}` : base.canvas,
          fps: fpsStats(stamps),
          unityFps: unityFps(startFrames, window.__unityFrames, elapsed),
        };
        window.__spikeResult = done;
        console.info('GV_SPIKE_RESULT ' + JSON.stringify(done));
        setResult(done);
      }
    };
    frame = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(frame);
  }, [isLoaded, build, sceneId, dpr]);

  const copy = async () => {
    if (!result) return;
    try {
      await navigator.clipboard.writeText(formatResult(result));
      setCopied(true);
    } catch {
      setCopied(false); // the text stays selectable in the panel below
    }
  };

  return (
    <div className="flex flex-col gap-24">
      <header className="flex flex-col gap-8">
        <span className="type-label text-brand-volt">Spike · Prompt 0.7</span>
        <h1 className="type-display text-headline font-bold">Web performance</h1>
        <nav className="flex flex-wrap gap-8" aria-label="Scene">
          {(Object.keys(SCENES) as SceneId[]).map((id) => (
            <Link
              key={id}
              to={`/spike?scene=${id}`}
              reloadDocument
              aria-current={id === sceneId ? 'page' : undefined}
              className={buttonClasses({ variant: id === sceneId ? 'primary' : 'secondary', size: 'sm', pill: true })}
            >
              {SCENES[id].label}
            </Link>
          ))}
        </nav>
      </header>

      <div className="relative aspect-[3/4] w-full max-w-2xl overflow-hidden rounded-lg border border-border-subtle bg-bg-inset">
        <Unity
          unityProvider={unityProvider}
          devicePixelRatio={dpr}
          ref={canvas}
          className="h-full w-full"
          tabIndex={-1}
        />
        {!isLoaded && !initialisationError && (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-12 p-24">
            <span className="text-body-sm text-text-secondary">Loading {label}</span>
            <div className="h-8 w-full max-w-xs overflow-hidden rounded-pill bg-bg-elevated" role="progressbar" aria-valuenow={Math.round(loadingProgression * 100)}>
              <div className="h-full rounded-pill bg-brand-volt" style={{ width: `${Math.round(loadingProgression * 100)}%` }} />
            </div>
            <span className="text-caption text-text-muted">{Math.round(loadingProgression * 100)}%</span>
          </div>
        )}
        {initialisationError && (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-8 p-24 text-center">
            <span className="type-label text-state-danger">Failed to start</span>
            <p className="text-body-sm text-text-secondary">{initialisationError.message}</p>
          </div>
        )}
      </div>

      <section className="flex max-w-2xl flex-col gap-12 rounded-lg border border-border-subtle bg-bg-surface p-16">
        <div className="flex items-center justify-between gap-12">
          <h2 className="type-display text-title-sm font-bold">Results</h2>
          <Button size="sm" variant="secondary" pill disabled={!result?.fps} onClick={copy}>
            {copied ? 'Copied' : 'Copy results'}
          </Button>
        </div>
        <pre className={cn('whitespace-pre-wrap break-words select-all text-caption text-text-secondary', !result && 'text-text-muted')}>
          {result ? formatResult(result) : 'Waiting for the build to load...'}
        </pre>
      </section>
    </div>
  );
}
