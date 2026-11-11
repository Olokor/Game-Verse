# Web performance spike: phone test checklist (Prompt 0.7)

Throwaway. Deleted together with the spike code when ADR-0002 is written.

The `/spike` page loads one of two Unity Web builds and measures them:

| Scene | What it stresses |
|---|---|
| **2D: 200 sprites** | 200 flipbook-animated sprites, one script each (board, card and 2D arcade games) |
| **3D: car, baked light** | A car lapping a track, baked lightmaps, realtime car shadow, Forward+ renderer (3D racers) |

The results panel shows:

| Line | Meaning |
|---|---|
| build download | Size of the build files (Brotli), what a player downloads the first time |
| downloads finished | When the last build file arrived (depends on the network) |
| startup | Time from "downloaded" to "playable": decompression, compile, engine start. **This is the phone's CPU, not the network** |
| playable | Total time from opening the link to a running game |
| unity fps | Frames the game actually rendered per second, over 10 s after a 2 s warm-up. **The main smoothness number** |
| browser fps avg / 1% low / hitches | The browser's own frame rate in the same window: the ceiling for the game. 1% low and hitches (frames over 50 ms) show stutter. A flat 30 here usually means iOS Low Power Mode or "Limit Frame Rate" is on |
| gpu | The phone's GPU as the browser reports it |

## Before you start

1. I run the spike server and the tunnel on the PC and send you a link like `https://something.trycloudflare.com/spike`.
   - It works from any network (Wi-Fi or mobile data).
   - It only exists while I'm running it.
   - Download times include the PC's upload speed, which is why **startup** is reported separately.
2. Charge both phones above 50% and close other apps.
3. Turn off Low Power Mode (iPhone) or battery saver (Android). They throttle the CPU, and on iPhone they cap the browser at 30 fps. On iPhone also check Settings → Accessibility → Motion → Limit Frame Rate is off.

## For each phone (iPhone with Safari, low-end Android with Chrome)

Note the model, OS version and browser version (Settings → About).

### A. Cold load: first visit
1. Open the link. It starts on **2D: 200 sprites**.
2. Wait until the panel shows an `fps` line (about 15 s after the game appears).
3. Tap **Copy results** and paste it into a note. If copying doesn't work, long-press the text to select and copy it.
4. Tap **3D: car, baked light** and repeat steps 2 and 3.

### B. Warm load: second visit (cached)
5. Close the tab completely and open the link again.
6. Repeat steps 2 to 4. The "on the wire" size should now be close to 0 MB.

### C. Feel test: 60 s per scene (no numbers needed, just notes)
7. Let each scene run for a full minute. Look for:
   - stutter or freezes;
   - the phone getting noticeably warm;
   - the page reloading by itself or "A problem repeatedly occurred" (Safari), which means a memory crash;
   - the game going black.
8. Rotate the phone to landscape and back. Does it keep running and stay sharp?
9. Switch to another app for 10 s and come back. Does it resume, or reload or go black?

### D. Report back
Paste me, per phone:
- the model, OS and browser;
- the four results blocks (2D cold, 3D cold, 2D warm, 3D warm);
- your notes from part C.

## What I measure myself
- This PC's Chrome, on the real GPU, unthrottled and with 4x CPU slowdown plus Fast 4G network emulation.
- The Samsung Note 9 over USB (Chrome).

Those numbers go in ADR-0002 next to yours.
