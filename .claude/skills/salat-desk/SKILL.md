---
name: salat-desk
description: How the Salat Desk always-on (AOD) prayer clock is built and shipped. It covers the .NET MAUI app with a shared HTML/JS dashboard and an Android APK/AAB, and how to build the iPhone/iPad version on a Mac. Use this skill for any work on this repo: building or installing the Android app, signing a release, Google Play publishing, starting or continuing the iOS/iPadOS port on a MacBook, burn-in protection, ambient/night modes, Ayat/Hadith rotation, Jamaat alerts and notifications, or adding a new display or alert feature (for example an on-screen notification dot). Also use it whenever someone wants to make another always-on / AOD-style app for an old phone or tablet, even if they don't mention Salat Desk.
---

# Salat Desk: building an always-on (AOD) app

Salat Desk turns an old phone into an always-on desk prayer clock. This skill covers three things:
- how the app is put together;
- the always-on design rules that keep an AMOLED screen alive;
- how to build, release and port it.

For the deeper details (API tables, data model, content pipeline), read `docs/DEVELOPMENT.md`. This skill covers
the reasoning and the steps that are easy to get wrong.

Pick the reference that matches the task:

| Task | Read |
| --- | --- |
| Build the iPhone/iPad version on a Mac | `references/ios-on-mac.md` |
| Publish to Google Play (AAB, listing, policies) | `references/google-play.md` and `docs/google-play/` |
| Planned feature: a notification dot on screen | `references/notification-dot.md` |
| Anything else | This file, then `docs/DEVELOPMENT.md` |

## How the app is put together

```text
src/DeskDashboard.Web/wwwroot   HTML/CSS/JS dashboard: the one UI, shared by every platform
src/DeskDashboard.Core          all logic in plain .NET: prayer times (Adhan), Jamaat rules, SQLite (EF Core),
                                Ayat/Hadith content and weekly refresh, LocalApi (an in-process HTTP-style API)
src/DeskDashboard.App           .NET MAUI shell: HybridWebView + a small native bridge + platform code
src/DeskDashboard.Web           optional PC web host serving the same wwwroot over HTTP
```

**Why it is built this way.** The dashboard is HTML so the same page runs in a desktop browser for fast iteration
and in the app. Inside the app, `location.hostname === '0.0.0.1'` (the HybridWebView origin) makes the page call
`window.HybridWebView.InvokeDotNet('Api', [method, url, body])` instead of `fetch`. That lands in
`MainPage.Bridge.Api` → `LocalApi.HandleAsync`, which answers exactly like the ASP.NET endpoints. A new platform
therefore only needs the MAUI shell and its platform code. Core and the UI do not change.

**Native bridge methods** (`src/DeskDashboard.App/MainPage.cs`):
- `Api(method, url, body)`;
- `SetBrightness(level)`;
- `ScheduleJamaatAlerts(json)`.

Anything platform-specific lives behind `#if ANDROID` / `#if IOS` in the App project, or in
`Platforms/<platform>/`.

## The always-on recipe (reuse this for any AOD-style app)

An app can't use the phone's real AOD (that is a system feature). It has to stay in front with the screen on, and
not destroy the screen. These rules do that; each one has a reason:

1. **Keep the screen on only while the app is visible.** Set `DeviceDisplay.Current.KeepScreenOn = true` in
   `OnAppearing` and false in `OnDisappearing`. No wake lock or service is needed, and the user can still leave
   the app.
2. **Pure black background, grey text.** AMOLED pixels that show `#000` are off, so they don't wear and use no power.
   Avoid white and saturated colours in the ambient view.
3. **Move everything every minute.** The ambient block jumps to a new position from a low-discrepancy sequence
   (`AmbientState.blockPosition`), with a fade. The full view shifts by ±3 px. Static pixels are what burn in.
4. **Hide the system bars** (immersive mode on Android, `PrefersStatusBarHidden` +
   `PrefersHomeIndicatorAutoHidden` on iOS). The clock and battery icons would otherwise stay lit in one place all
   day.
5. **Dim the app's own window, not the system.** On Android, `Window.Attributes.ScreenBrightness` needs no
   permission and resets when the app leaves. iOS has only the global `UIScreen.MainScreen.Brightness`, so save and
   restore it (see the iOS reference).
6. **Three display modes, decided by one pure function every second** (`AmbientState.decideMode`):
   - focus: the full dashboard, around prayer times, after a tap, during a Jamaat warning;
   - ambient: a dim moving clock plus an Ayah/Hadith;
   - night: a tiny, dimmer clock.

   Keeping the rules pure means they are unit-tested in `tests/js/ambient-state.test.js` without a device.
7. **An AOD button** forces ambient until the current focus window ends (`{ skipUntil }`). A tap returns to focus.
   A ghost click can arrive right after pressing AOD, so clicks within 800 ms are ignored.
8. **Never show part of a holy text.** `measureFit` lays each item out invisibly at the real screen size, with fit
   steps 0 → 1 → 2, and skips the item if it doesn't fit. Fit is re-checked after fonts load (`document.fonts.ready`)
   and on resize (`ResizeObserver`), because the Arabic font changes the size after the first layout.
9. **Alerts must survive the app being in the background:** schedule them with the OS (Android `AlarmManager`,
   iOS `UNUserNotificationCenter`), not with JS timers. The page computes the schedule (`JamaatState.alertSchedule`)
   and the native side only schedules it. The schedule is replaced whenever data or settings change.

## Everyday commands (Windows PC)

```powershell
dotnet test tests/DeskDashboard.Tests          # C# tests (Core)
node --test tests/js/*.test.js                 # JS tests (pure modules + content bundle)
dotnet run --project src/DeskDashboard.Web --urls http://localhost:5080   # dashboard in a desktop browser
dotnet build src/DeskDashboard.App -f net10.0-android -t:Install          # debug build onto a USB phone
powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1  # signed APK -> dist\
powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1 -Format aab  # Play bundle -> dist\
```

Gotchas learned the hard way:
- **SDK and JDK paths:** the Android SDK and JDK live in `%LOCALAPPDATA%\Android\Sdk` and
  `%LOCALAPPDATA%\Microsoft\jdk`, and their paths go in `src/DeskDashboard.App/DeskDashboard.App.csproj.user`
  (git-ignored). If `keytool` or `adb` is "not recognized", run the `InstallAndroidDependencies` command in
  `docs/DEVELOPMENT.md` in the user's own shell.
- **Inspecting the page:** Debug builds can be inspected from `chrome://inspect`; `deskDashboardDebug()` in the
  console shows the mode, AOD state and preferences. Release builds cannot be inspected. Use
  `adb exec-out screencap -p > shot.png` and `adb logcat --pid=$(adb shell pidof com.salatdesk.app)`.
- **Locked phone:** a launch while the phone is locked shows a white screenshot. Wake it first with
  `adb shell input keyevent KEYCODE_WAKEUP` and `adb shell wm dismiss-keyguard`.
- **Checking alarms and notifications:** `adb shell dumpsys alarm | grep -A3 salatdesk` and
  `adb shell dumpsys notification --noredact`. The LED only blinks with the screen off, so a human has to look.
- **Database schema:** `EnsureCreated` doesn't add tables to an existing database. New tables go in
  `DashboardDatabase.ContentSchemaSql` (`CREATE TABLE IF NOT EXISTS`).
- **Release trimming:** trimming is `partial`, and Core and Adhan are trimmer roots because EF Core and Adhan use
  reflection. Always install and open a Release build before shipping.
- **Signing key:** the signing key is `%USERPROFILE%\.salatdesk\salatdesk.keystore`, alias `salatdesk`. Never commit
  it or put its password in a file or command line. The release script asks for the password and keeps it only in
  an environment variable for the build. Losing the key means the app can never be updated.

## Secrets and accounts

The user signs in, pays and types passwords themselves:
- the GitHub login, signing key password and Apple ID;
- the Google Play Console account and its $25 fee;
- the Apple Developer Program and its $99/year fee.

Never ask for or paste tokens or passwords into chat, files or commands. Use `gh` after the user has run
`gh auth login`. If `git push` hangs on the Windows Credential Manager, push with
`git -c credential.helper= -c "credential.helper=!'C:/Program Files/GitHub CLI/gh.exe' auth git-credential" push`.

## Releasing a new version

1. Bump `ApplicationDisplayVersion` (for example 0.2.0) and `ApplicationVersion` (an integer that must go up for
   every Play upload) in `src/DeskDashboard.App/DeskDashboard.App.csproj`.
2. Run both test suites.
3. Build: the APK for GitHub, the AAB for Play (the user types the key password in their own terminal).
4. Install the APK on a phone (`adb install -r dist/SalatDesk-<v>.apk`) and check the dashboard, the ambient view
   and the library.
5. Commit, push, then `gh release create v<version> dist/SalatDesk-<version>.apk --notes-file <notes>`.
6. Upload the AAB in Play Console (see `references/google-play.md`).
