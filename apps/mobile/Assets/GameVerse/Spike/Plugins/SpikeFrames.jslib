// Prompt 0.7 spike (throwaway, deleted with ADR-0002): exposes how many frames Unity has rendered, so the /spike page
// measures Unity's real frame rate instead of the browser's requestAnimationFrame rate.
mergeInto(LibraryManager.library, {
  GV_SpikeFrame: function (frame) {
    window.__unityFrames = frame;
  },
});
