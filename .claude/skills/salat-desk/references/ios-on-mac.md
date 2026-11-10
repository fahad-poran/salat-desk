# Building Salat Desk for iPhone and iPad on a Mac

The iOS app reuses everything except the MAUI shell's platform code:
- **Shared, no changes:** Core (prayer times, SQLite, content, `LocalApi`) and the `wwwroot` dashboard.
- **To write:** the iOS project files, brightness, hiding the status bar, and Jamaat notifications.

iOS apps can only be built on a Mac, because they need Xcode.

## Contents
1. One-time Mac setup
2. Project changes
3. iOS platform code (brightness, full screen, notifications)
4. Running on your own iPhone/iPad
5. Things to verify early
6. Burn-in on Apple screens
7. Distribution and cost

## 1. One-time Mac setup

1. Install **Xcode** from the App Store. Open it once to accept the licence and install the iOS platform, then run
   `sudo xcode-select -s /Applications/Xcode.app`. If the .NET build complains, it names the Xcode version it
   wants.
2. Install the **.NET 10 SDK** (macOS Arm64 installer from dotnet.microsoft.com), then:

   ```bash
   sudo dotnet workload install maui-ios
   ```

   `maui` installs every platform. `maui-android` is only needed if you also build Android on the Mac (then the
   Android SDK and JDK go through `-t:InstallAndroidDependencies`, as on Windows).
3. Clone the repo, and install Node.js for the JS tests:

   ```bash
   git clone https://github.com/fahad-poran/salat-desk.git
   ```

4. Check that nothing is broken before you start:

   ```bash
   dotnet test tests/DeskDashboard.Tests
   node --test tests/js/*.test.js
   ```

5. Sign in to Xcode with your Apple ID: Xcode → Settings → Accounts.

## 2. Project changes (`src/DeskDashboard.App/DeskDashboard.App.csproj`)

Add iOS only when building on a Mac, so the Windows Android build keeps working:

```xml
<TargetFrameworks>net10.0-android</TargetFrameworks>
<TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('osx'))">$(TargetFrameworks);net10.0-ios</TargetFrameworks>
```

The minimum OS version must be per platform; the current unconditional `24.0` is Android-only:

```xml
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'">24.0</SupportedOSPlatformVersion>
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">15.0</SupportedOSPlatformVersion>
```

To get the iOS project files, create a throwaway template and copy them over, so they match the installed
workload:

```bash
dotnet new maui -n Tmp -o /tmp/mauitmp
```

Copy `/tmp/mauitmp/Platforms/iOS/` (`AppDelegate.cs`, `Program.cs`, `Info.plist`, and `Resources/PrivacyInfo.xcprivacy`
if present) to `src/DeskDashboard.App/Platforms/iOS/`. Then change the namespace to `DeskDashboard.App`.

**`Info.plist` keys to set:**
- `CFBundleDisplayName` = Salat Desk.
- `UIDeviceFamily` = 1 (iPhone) and 2 (iPad).
- All four orientations for iPad, and portrait + both landscapes for iPhone. Supporting every orientation on iPad
  avoids needing `UIRequiresFullScreen`.
- `UIViewControllerBasedStatusBarAppearance` = true, so the page can hide the status bar.
- `ITSAppUsesNonExemptEncryption` = false (the app only uses HTTPS, which is exempt). This saves a question at
  every upload.

**`PrivacyInfo.xcprivacy`:** Apple requires reasons for some APIs that .NET itself uses. Declare:
- `NSPrivacyAccessedAPICategoryFileTimestamp` with reason `C617.1`;
- `NSPrivacyAccessedAPICategorySystemBootTime` with `35F9.1`;
- `NSPrivacyAccessedAPICategoryDiskSpace` with `E174.1`;
- `NSPrivacyAccessedAPICategoryUserDefaults` with `CA92.1` (MAUI `Preferences` uses NSUserDefaults).

Set `NSPrivacyTracking` false and no collected data types. Check Microsoft's current ".NET MAUI privacy manifest"
page in case the list has changed.

**`JamaatAlert` record:** move it from `Platforms/Android/JamaatAlerts.cs` to a shared file (for example
`src/DeskDashboard.App/JamaatAlert.cs`) so both platforms use it.

## 3. iOS platform code

### Keep the screen on
`DeviceDisplay.Current.KeepScreenOn` already works on iOS (it sets `IdleTimerDisabled`). Nothing to add.

### Full screen (hide the status bar and the home indicator)
In `MainPage`'s constructor:

```csharp
#if IOS
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
// ...
On<iOS>().SetPrefersStatusBarHidden(StatusBarHiddenMode.True);
On<iOS>().SetPrefersHomeIndicatorAutoHidden(true);
On<iOS>().SetUseSafeArea(false);   // the page handles safe areas itself with env(safe-area-inset-*)
#endif
```

**Notch check:** look at the ambient block on a notched or Dynamic Island iPhone. If the block can move under the
notch, add `padding: env(safe-area-inset-top) …` to the ambient container in `dashboard.css`, and make sure
`index.html` has `viewport-fit=cover` in its viewport meta.

### Brightness
iOS has no per-window brightness. `UIScreen.MainScreen.Brightness` is the whole phone's brightness and it stays
after the app leaves. So:
- **Save:** store the user's level when the app becomes active.
- **Set:** in `WindowBrightness.Set`, under `#elif IOS`, set `UIScreen.MainScreen.Brightness = value < 0 ? saved : value`.
- **Restore:** put the saved level back in `OnResignActivation`.

Wire the lifecycle in `MauiProgram`:

```csharp
builder.ConfigureLifecycleEvents(events =>
{
#if IOS
    events.AddiOS(ios => ios
        .OnActivated(_ => WindowBrightness.CaptureSystemLevel())
        .OnResignActivation(_ => WindowBrightness.RestoreSystemLevel()));
#endif
});
```

Expect a few small bugs here: Auto-Brightness can fight the value. Test with Auto-Brightness on and off.

### Jamaat notifications (`Platforms/iOS/JamaatAlerts.cs`)
These are local notifications only; no push server or Apple Push certificate is needed.

- **Permission:** once, `UNUserNotificationCenter.Current.RequestAuthorizationAsync(Alert | Sound | Badge)`. Do it
  in `ScheduleJamaatAlerts` when the list is not empty, as the Android code does for Android 13+.
- **Schedule:**
  1. Remove the old ones: get `GetPendingNotificationRequestsAsync()` and remove the IDs starting with `jamaat|`.
  2. Add one `UNNotificationRequest` per alert, with ID `"jamaat|" + alert.Id`.
  3. Give each a `UNTimeIntervalNotificationTrigger.CreateTrigger((WarnAt - now) / 1000.0, false)`.
  4. Use `UNMutableNotificationContent` with the same title and text as Android, and `Sound = UNNotificationSound.Default`.
- **Limit:** iOS keeps at most 64 pending notifications. The page sends about 6, so this is fine.
- **Showing it while the app is open:** this matters for an always-on app. iOS does not show a notification while
  the app is in front unless a delegate allows it. Set `UNUserNotificationCenter.Current.Delegate` to a class whose
  `WillPresentNotification` calls `completionHandler(Banner | List | Sound)`. Do it in `AppDelegate.FinishedLaunching`,
  before `base`.
- **Time Sensitive (optional):** setting `InterruptionLevel = TimeSensitive` lets alerts through Focus modes. It
  needs the Time Sensitive Notifications capability in the provisioning profile, so add it only with a paid
  developer account.
- **No light:** iPhones and iPads have no notification LED. The on-screen red frame, the sound and the vibration
  are the alert. Users who want a flash can turn on Settings → Accessibility → Audio/Visual → LED Flash for Alerts;
  the app can't control it.

Then, in `MainPage.Bridge.ScheduleJamaatAlerts`, change `#else` into `#elif IOS` and call the iOS scheduler,
keeping a final `#else return 0;`.

## 4. Running on your own iPhone/iPad

**Simulator first** (fast, no signing):

```bash
dotnet build src/DeskDashboard.App -t:Run -f net10.0-ios
```

**Real device:**
1. **Developer Mode:** connect the device with a cable, trust the Mac, and turn on Settings → Privacy & Security →
   Developer Mode (iOS 16+).
2. **Free signing setup**, once per bundle ID (MAUI cannot create free profiles itself, Xcode can):
   1. In Xcode create a new iOS App project with bundle identifier `com.salatdesk.app`, and choose your Personal
      Team under Signing.
   2. Run that empty app on the device once. This creates the signing certificate and the provisioning profile.
3. **Build and run:**

   ```bash
   xcrun devicectl list devices          # find the device identifier (UDID)
   dotnet build src/DeskDashboard.App -t:Run -f net10.0-ios -p:RuntimeIdentifier=ios-arm64 -p:_DeviceName=<UDID>
   ```

   If it can't choose a signing identity, add `-p:CodesignKey="Apple Development: <your name> (<id>)"`. You can list
   identities with `security find-identity -p codesigning -v`.
4. **Trust the certificate:** on the device, Settings → General → VPN & Device Management, and trust your developer
   certificate.

**Expiry:** free signing expires after **7 days**; then run the build again. With the paid program a build lasts a
year, and TestFlight is available.

**For a desk display:** turn on Guided Access (Settings → Accessibility) and triple-click to lock the iPad/iPhone
into Salat Desk.

## 5. Things to verify early (most likely to break on iOS)

- **EF Core under AOT:** iOS doesn't allow JIT. The symptom is a crash at start with "Attempting to JIT compile".
  Try `<UseInterpreter>true</UseInterpreter>` in an iOS-only PropertyGroup. Test a **Release** build on a real
  device early, not only Debug in the simulator.
- **SQLite:** `Microsoft.EntityFrameworkCore.Sqlite` brings `SQLitePCLRaw.bundle_e_sqlite3`, which supports iOS.
  Check that `dashboard.db` gets created (look for "Importing the starter content failed" in the log).
- **localStorage:** the HybridWebView origin on iOS is `app://0.0.0.1` (the hostname is still `0.0.0.1`, so the
  bridge detection works). Confirm that `localStorage` survives killing the app, because the alert and display
  preferences live there. If it doesn't, move them into `LocalApi` (SQLite).
- **Notifications:** check that a Jamaat warning appears as a banner while the app is open (this needs the
  delegate from section 3).
- **Weekly download:** the Wi-Fi check uses MAUI `Connectivity`, which works on iOS. The download only runs while
  the app is open, which is fine for an always-on app.

## 6. Burn-in on Apple screens

| Device | Screen | What it means |
| --- | --- | --- |
| iPhone X, XS, 11 Pro, 12 and later (not the SE or 11) | OLED | Same risk as Samsung AMOLED: all the burn-in rules apply. |
| iPhone XR, 11, SE | LCD | No burn-in, but black still glows, so dim more. |
| iPad (all except iPad Pro M4) | LCD | Same as the LCD iPhones. |
| iPad Pro M4 and later | OLED | All the burn-in rules apply. |

Keep the moving block, the dimming and the black background on every device: the code is the same anyway.

## 7. Distribution and cost

| Option | Cost | Notes |
| --- | --- | --- |
| Free Apple ID + Xcode | Free | Your own devices only. The build expires after 7 days. At most 3 apps per device. |
| Apple Developer Program | US$99 per year | Builds last a year, TestFlight to up to 10,000 testers, App Store. |
| App Store release | Included in the program | Needs App Store Connect: listing, privacy "nutrition label" (no data collected), screenshots for 6.9" iPhone and 13" iPad, and review. |

Make the App Store build with:

```bash
dotnet publish src/DeskDashboard.App -f net10.0-ios -c Release -p:ArchiveOnBuild=true \
  -p:RuntimeIdentifier=ios-arm64 -p:CodesignKey="Apple Distribution: …" -p:CodesignProvision="<profile name>"
```

Upload the `.ipa` with Apple's **Transporter** app.
