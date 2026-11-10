# Planned feature: a notification dot on screen

**The wish:** while a Jamaat warning is active, show a small red dot at the top of the screen, in the spot where
iOS shows its microphone/camera privacy dot.

## What is and isn't possible

- **iOS:** the privacy dot (green, orange or red) is drawn by iOS itself, above every app, and only for
  camera/microphone/location use. No app can draw there or reuse it, and App Review would reject trying. Salat Desk
  hides the status bar anyway (burn-in), so the app owns the whole screen. It can draw **its own** dot in the same
  place: the top-right corner beside the notch or Dynamic Island, inside the safe area.
- **Android:** apps can't draw in the system status bar either, and Salat Desk hides it too. Draw the dot in the
  page, beside the camera cutout. In the WebView, `env(safe-area-inset-top)` gives the cutout area once
  `viewport-fit=cover` is set.
- **App in the background:** the dot can't show, because nothing of the app is on screen. Then the OS notification
  (and the LED on Android) does the job. On iPhones with a Dynamic Island, a Live Activity could show a countdown
  there. That needs a native Swift widget extension (ActivityKit), which is a separate, larger project.

## Suggested implementation (shared page, works on every platform)

1. Add `<div id="alertDot" class="alert-dot" hidden></div>` in `index.html`, beside `#warningFrame`.
2. **CSS:**
   - `position: fixed`, `top: max(6px, env(safe-area-inset-top) * 0.35)`, `right: 18%` (near the corner, clear of
     the notch).
   - 10 px circle, `background: #ff3b30`, a gentle blink (1 s), `z-index` above the frame, `pointer-events: none`.
   - Respect `prefers-reduced-motion`.
3. **JS (`applyCountdownView` in `app.js`):** show the dot while the phase is `approaching` (the same condition as
   `#warningFrame`), in every mode, including AOD/night.
4. **Setting:** add "Show alert dot" in the Jamaat alerts section, default on, stored with the other alert
   preferences.
5. **Burn-in:** it is only on during warning minutes and it blinks, so the risk is negligible. Still, move it a few
   pixels each minute like everything else.
6. **Test:** add JS tests for any new pure logic. Check on the device during a short test warning (set one prayer's
   Jamaat a few minutes ahead), then restore the user's times.
