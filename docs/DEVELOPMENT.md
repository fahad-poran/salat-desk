# Salat Desk: developer guide

How the app is built, how the pieces fit, and how to build, test and release it. For what the app does
and how to use it, see the [README](../README.md).

## Architecture

```text
┌───────────────────────────── Android app (.NET MAUI, net10.0-android) ─────────────────────────────┐
│  HybridWebView  ── wwwroot (HTML/CSS/JS dashboard) ──►  window.HybridWebView.InvokeDotNet('Api')    │
│        │                                                               │                           │
│        ▼                                                               ▼                           │
│  MainPage.Bridge.Api(method, url, body)  ───────────────►  LocalApi (DeskDashboard.Core)           │
│  MainPage.Bridge.SetBrightness(level)  ─► WindowBrightness           │                             │
│  MainActivity: immersive mode, keep screen on                         ▼                             │
│                                              PrayerService · ContentService · SQLite (dashboard.db) │
│  ContentRefreshLoop (background, Wi-Fi only) ─► ContentRefreshService ─► AlQuran.cloud / hadith-api │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘

PC web host (optional, ASP.NET Core): serves the same wwwroot and API at http://<pc>:5080 for a browser.
```

The dashboard UI is written once (`src/DeskDashboard.Web/wwwroot`). In a normal browser it calls the API with
`fetch`; inside the app (`location.hostname === '0.0.0.1'`) the same calls go through the HybridWebView bridge to
`LocalApi`, which returns the same status codes and camelCase JSON as the ASP.NET endpoints.

### Projects

| Project | Purpose |
| --- | --- |
| `src/DeskDashboard.Core` | All logic: prayer calculation (Adhan), Jamaat windows and validation, EF Core SQLite data, Ayat/Hadith content, weekly refresh, `LocalApi`. No ASP.NET or MAUI dependencies. |
| `src/DeskDashboard.App` | Android app (MAUI). Hosts the dashboard in a HybridWebView, the bridge, brightness, immersive mode. |
| `src/DeskDashboard.Web` | PC web host and the source of the shared `wwwroot`. Prayer endpoints, plus `/api/content/*` forwarded to `LocalApi`. |
| `tests/DeskDashboard.Tests` | xUnit tests for Core. |
| `tests/js` | `node:test` tests for the pure frontend modules and the content bundle. |
| `tools/content` | Builds the starter Ayat/Hadith set and an offline review page. |

Project and namespace names keep the original `DeskDashboard.*` prefix; the product name is **Salat Desk**
(`ApplicationTitle`, `ApplicationId com.salatdesk.app`, page title).

## Frontend modules (`wwwroot/js`)

| File | Role |
| --- | --- |
| `jamaat-state.js` | Pure: countdown phase for the current prayer (upcoming, waiting, approaching ≤10 min, reached, passed, missing, invalid). |
| `ambient-state.js` | Pure: display-mode rules (focus / ambient / night), the AOD override, block position per minute, which Ayah/Hadith comes next. |
| `app.js` | DOM, API calls, the native bridge, rendering, Settings, Library, ambient rendering and fitting. |

### Display modes

`AmbientState.decideMode()` runs every second:

1. **Focus** (full dashboard): Settings or Library open, for 2 minutes after a tap, during the 10-minute Jamaat
   warning, or inside a prayer's focus window (default 15 min before to 10 min after the Jamaat, or the Waqt when no
   Jamaat is set).
2. **Night**: from the night start (default 23:00) until the next Fajr Waqt.
3. **Ambient**: everything else.

The **AOD button** stores `{ skipUntil }` = end of the focus window running at the time (or now). Focus reasons
before `skipUntil` are ignored; the first focus reason after it ends AOD (`aodEnded`). A tap ends AOD at once.

Brightness per mode is set for the app window only (`WindowBrightness`, no permission): focus = system, ambient =
8 %, night = 2 % by default.

### Burn-in protection

- Pure black background (AMOLED pixels off) and grey text only.
- The ambient block moves every minute to a position from a low-discrepancy sequence (`blockPosition`), with a
  fade-out, swap, fade-in.
- The full view shifts the whole page by up to ±3 px each minute.
- The status and navigation bars are hidden (immersive mode).
- The Ayah/Hadith text itself changes every minute.

### Never showing part of a holy text

`app.js` guarantees that an Ayah/Hadith is either shown completely or not at all:

- `measureFit(item)` renders the item in an invisible copy of the ambient block at the current screen size and
  returns the fit step it needs (0, 1 = hide date/times and shrink the clock, 2 = also shrink the text) or −1.
  Results are cached per item and screen size, but only once fonts have loaded.
- `selectContent()` asks `AmbientState.chooseNext()` for the least recently shown item and skips anything that
  measures −1. Those items stay readable in full, with scrolling, in the Library.
- `placeAmbient()` fits and positions the block inside the screen; a `ResizeObserver` and `document.fonts.ready`
  re-fit and re-place it if its size changes after placement (for example when the Arabic font finishes loading).

## Jamaat alerts

- **Warning length** is per prayer (`deskDashboard.jamaatAlertPrefs` in localStorage:
  `{ fajr: { notify, minutes }, … }`, normalised by `JamaatState.normalizeAlertPrefs`). `JamaatState.evaluate(entries,
  now, { warningMinutes })` starts the `approaching` phase at `jamaatAt − min(minutes, jamaatAt − waqtAt)`, so the
  warning never starts before the Waqt. Default 10 minutes.
- **On screen** while `approaching`: the next-prayer panel and its countdown blink (`jamaat-warning-pulse`), a fixed
  full-screen red frame (`#warningFrame`) blinks above every mode, and in ambient/night the block gets `is-warning`
  with a red `Asr Jamaat in mm:ss` line. "Blink the warning" off keeps everything red but still; reduced motion
  disables the animation.
- **Notifications (Android)**: `scheduleNativeAlerts()` sends `JamaatState.alertSchedule()` (upcoming warnings for
  prayers with Notify on: today and tomorrow's Fajr) to `MainPage.Bridge.ScheduleJamaatAlerts(json)` after each data
  load and Settings change. `Platforms/Android/JamaatAlerts.cs` cancels the previous alarms and sets
  `AlarmManager.SetExactAndAllowWhileIdle` per warning (inexact `SetAndAllowWhileIdle` on Android 12+ without the
  exact-alarm permission). `JamaatAlertReceiver` posts the notification on channel `jamaat_alerts` (high importance,
  red light, vibration). Android 13+ asks for `POST_NOTIFICATIONS` the first time alerts are scheduled. Alarms do not
  survive a reboot; the app reschedules them when it opens.
- Check on a device: `adb shell dumpsys alarm | grep -A3 salatdesk` and `adb shell dumpsys notification --noredact`.

## Content (Ayat and Hadith)

### Data model (SQLite)

`ContentItems`: `Type` (quran/hadith), `SourceKey` (unique: `quran:2:45`, `quran:94:5-8`, `bukhari:6168`,
`muslim:2609a`), `Ref`, `Source`, `Grade`, `Arabic`, `English`, `Batch` (`seed` or ISO week `2026-W46`),
`AddedAtMs`, `LastShownAtMs`, `ShowCount`, `Hidden`, `HiddenAtMs`.

`ContentStates` (one row): `SeededAtMs`, `LastRefreshAtMs`, `LastAttemptAtMs`, `LastResult`, last added counts.

`EnsureCreated` does not add tables to an existing database, so `DashboardDatabase.ContentSchemaSql` creates them
with `CREATE TABLE IF NOT EXISTS`. Keep it in step with `DashboardDbContext`.

Removed items keep their row with `Hidden = 1`, so a refresh never adds them back.

### Starter set

`src/DeskDashboard.Core/Content/seed-content.json` is embedded in Core and imported once, in the background, on
first run (`ContentService.SeedIfNeededAsync`). It is generated by:

```powershell
node tools/content/build-content.mjs      # downloads ~30 MB of sources into tools/content/.cache
node --test tests/js/content-bundle.test.js
```

The script also validates every reference in the Quran pool and lists any that are too long to ever show.
`node tools/content/build-review.mjs` writes `docs/content-review.html` for reviewing the starter set offline;
references listed in `tools/content/exclude.txt` are left out.

### Weekly refresh

`ContentRefreshService.RefreshIfDueAsync()` runs at start-up (after 20–30 s) and every 6 hours via
`ContentRefreshLoop`; it downloads only when 7 days have passed since the last success. In the app it waits for
Wi-Fi/Ethernet (`CanDownload`). Settings → **Update now** calls `POST /api/content/refresh`.

- **Quran**: the next ~70 references from `Content/quran-pool.txt` (curated, after the `===== weekly =====`
  marker) that are not stored yet. Each ayah: `GET https://api.alquran.cloud/v1/ayah/{s}:{a}/editions/quran-uthmani,en.sahih`,
  one request per second (soft rate limit). The Basmala that the API prefixes to ayah 1 is removed; joined ayat get
  `﴿n﴾` markers.
- **Hadith**: section files from `https://cdn.jsdelivr.net/gh/fawazahmed0/hadith-api@1/editions/{eng|ara}-{book}/sections/{n}.min.json`
  (falling back to `.json`, as the API asks). Books: Bukhari 2, 3, 66, 75, 78, 79, 80, 81, 82, 97; Muslim 1, 43,
  45, 46, 47, 48, 49, 50, 53, 55; Nawawi 40; Qudsi 40. ~200 eligible hadith not stored yet are chosen at random
  (seeded by the week).
- **Filtering** (`ContentText`, mirrored in `build-content.mjs`): HTML removed, print hyphenation joined, narrations
  that depend on another (`the rest of the hadith is the same`, chain-only notes) and weak/fabricated grades
  excluded, length limits (Quran 360/400, Hadith 420/360 Arabic/English characters), standard numbering
  (`2609.01` → `2609a`).
- A failure stores nothing and is retried at the next check; existing content is never changed.

### Content API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/content?type=quran\|hadith&hidden=false` | Library: most recently shown first, then never shown |
| GET | `/api/content/active` | Everything not removed, for rotation |
| POST | `/api/content/{id}/shown` | Records that an item was displayed (204) |
| PUT | `/api/content/{id}/hidden` | `{ "hidden": true }` removes, `false` restores |
| GET | `/api/content/status` | Counts, last/next update, credits |
| POST | `/api/content/refresh` | Starts an update in the background (202) |

## Prayer logic

- Calculation: Adhan 0.9.0 (`PrayerTimeCalculator`) with method, angles, Asr madhab and per-prayer adjustments.
- Jamaat windows (`JamaatSchedule`), end-exclusive at minute precision: Fajr → Sunrise, Dhuhr → Asr, Asr →
  Maghrib, Maghrib → Isha, Isha → next Fajr (Isha may pass midnight). Saving outside a window returns 400.
- A settings change that invalidates a stored timetable keeps it and reports conflicts.
- `GET /api/prayer/today` returns the six times, the previous Isha and tomorrow's Fajr so midnight handovers need no
  extra request.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/prayer/today?date=YYYY-MM-DD` | Times, next prayer, windows, timetable, conflicts |
| GET / PUT | `/api/prayer/settings` | Location and calculation settings |
| GET / PUT | `/api/prayer/timetable` | Dated mosque Jamaat timetable |
| GET | `/api/location/search?q=` | City lookup (Open-Meteo geocoding) |

## Build

### Toolchain (Windows, one time)

1. .NET 10 SDK.
2. `dotnet workload install maui-android` (administrator).
3. Android SDK and Microsoft OpenJDK:

   ```powershell
   dotnet build src/DeskDashboard.App -t:InstallAndroidDependencies -f net10.0-android `
     "-p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk" `
     "-p:JavaSdkDirectory=$env:LOCALAPPDATA\Microsoft\jdk" -p:AcceptAndroidSDKLicenses=True
   ```

4. Put both paths in `src/DeskDashboard.App/DeskDashboard.App.csproj.user` (git-ignored):

   ```xml
   <Project><PropertyGroup>
     <AndroidSdkDirectory>C:\Users\YOU\AppData\Local\Android\Sdk</AndroidSdkDirectory>
     <JavaSdkDirectory>C:\Users\YOU\AppData\Local\Microsoft\jdk</JavaSdkDirectory>
   </PropertyGroup></Project>
   ```

### Run on a phone (debug)

Enable USB debugging on the phone, connect it, then:

```powershell
dotnet build src/DeskDashboard.App -f net10.0-android -t:Install
```

Inspect the WebView from Chrome on the PC at `chrome://inspect` (Debug builds enable WebView debugging).
`deskDashboardDebug()` in the console shows the current mode, wake timer, AOD state and preferences.

### Run the PC web host

```powershell
dotnet run --project src/DeskDashboard.Web --urls http://localhost:5080
```

Use `--urls http://0.0.0.0:5080` to reach it from other devices on the LAN, and allow TCP 5080 for the private
network profile only. It has no authentication: never expose it to the internet.

### Tests

```powershell
dotnet test DeskDashboard.slnx          # or tests/DeskDashboard.Tests to skip the Android build
node --test tests/js/*.test.js
```

## Release (signed APK)

Create a signing key once and keep it safe, outside the repository. Play Store updates must be signed with the
same key; if it is lost, the app cannot be updated.

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.salatdesk" | Out-Null
& "$env:LOCALAPPDATA\Microsoft\jdk\bin\keytool.exe" -genkeypair -v `
  -keystore "$env:USERPROFILE\.salatdesk\salatdesk.keystore" -alias salatdesk `
  -keyalg RSA -keysize 2048 -validity 10000
```

Build the signed APK, reading the password from an environment variable so it is not stored anywhere:

```powershell
$env:SALATDESK_KEY_PASS = Read-Host "Keystore password"
dotnet publish src/DeskDashboard.App -f net10.0-android -c Release `
  -p:AndroidPackageFormats=apk -p:AndroidKeyStore=true `
  "-p:AndroidSigningKeyStore=$env:USERPROFILE\.salatdesk\salatdesk.keystore" `
  -p:AndroidSigningKeyAlias=salatdesk `
  -p:AndroidSigningKeyPass=env:SALATDESK_KEY_PASS -p:AndroidSigningStorePass=env:SALATDESK_KEY_PASS
```

The APK is in `src/DeskDashboard.App/bin/Release/net10.0-android/publish/com.salatdesk.app-Signed.apk`.

`tools\release\build-release.ps1` does all of this and copies the result to `dist\`. Its `-Format aab` option builds
the App Bundle for Google Play. The Play checklist, listing text, Data safety answers and store graphics are in
[docs/google-play](google-play/README.md); `tools\play\make-graphics.ps1` regenerates the graphics. The privacy
policy is [PRIVACY.md](../PRIVACY.md).

Before each release: bump `ApplicationDisplayVersion` and `ApplicationVersion` in `DeskDashboard.App.csproj`, run
both test suites, and install the Release build on a phone (trimming is `partial`; EF Core, Core and Adhan are kept
as trimmer roots).

## Roadmap

- Google Play: account, closed test with 12 testers for 14 days, production (see [docs/google-play](google-play/README.md)).
- On-screen alert dot during a Jamaat warning (plan in `.claude/skills/salat-desk/references/notification-dot.md`).
- GPS location, Hijri date, Qibla, Adhan notifications.
- Bangla and Arabic interface (right-to-left).
- iOS and iPadOS: step-by-step guide in `.claude/skills/salat-desk/references/ios-on-mac.md`. The dashboard, Core
  and the bridge are shared.

## Claude Code skill

`.claude/skills/salat-desk/` is a project skill for Claude Code. Opening this repo in Claude Code (on Windows or a
Mac) makes it available automatically. It summarises the architecture, the always-on design rules, the build and
release steps, the iOS port and Google Play publishing.
