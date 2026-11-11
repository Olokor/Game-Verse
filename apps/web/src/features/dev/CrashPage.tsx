/** /dev/crash (development and screenshot builds only): fails on render to show the error screen (ADR-0005). */
export function CrashPage(): never {
  throw new Error('GV_DEV_CRASH: deliberate render error from /dev/crash');
}
