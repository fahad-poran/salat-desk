# Publishing Salat Desk on Google Play

Everything that can be prepared in the repo is in `docs/google-play/`:
- `README.md`: the step-by-step checklist;
- `listing.md`: name, descriptions, category;
- `data-safety.md`: answers for the Data safety and policy forms;
- `graphics/`: the store icon, feature graphic and screenshots.

The privacy policy is `PRIVACY.md`. Its public URL is
`https://github.com/fahad-poran/salat-desk/blob/main/PRIVACY.md`.

## What only the user can do
- Create the Play Console developer account, pay the one-time US$25 fee, and verify their identity.
- Find 12 testers for the closed test (personal accounts created after November 2023).
- Type the signing key password.

Never take card details, ID documents or passwords. You can guide them step by step, or fill in non-sensitive
listing text in the browser once they are signed in, asking before each submit.

## Build the bundle
Play needs an Android App Bundle (`.aab`), not an APK:

```powershell
powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1 -Format aab
```

This writes `dist\SalatDesk-<version>.aab`.

- **Version code:** every upload needs a higher `ApplicationVersion` than the last one Play has seen, even a
  rejected upload.
- **Target API level:** the app targets the API level of its .NET Android SDK (36 at the moment). Google raises the
  minimum target every August, so update .NET when Play Console warns about it.

## Signing on Play
Play App Signing is mandatory. When the first bundle is uploaded, choose to **use the existing key**: Play Console
→ App integrity → "Use a different key" → "Export and upload a key from Java keystore" (the PEPK tool).
- **Why:** with this choice, the APKs on GitHub and the Play builds have the same signature, so users can move
  between them without uninstalling.
- **Otherwise:** if Google generates a new key, a GitHub-installed app can't be updated from Play and vice versa.
- **Upload key:** the same `salatdesk` keystore can be the upload key.

## Policy points specific to this app
- **Exact alarms:** `SCHEDULE_EXACT_ALARM` needs no Play declaration, but on Android 14+ it is off by default for
  new installs. The app then falls back to inexact alarms, which can be a few minutes late.
  - Don't switch to `USE_EXACT_ALARM`: Play allows it only for alarm-clock and calendar apps.
  - A later improvement: a Settings button that opens `ACTION_REQUEST_SCHEDULE_EXACT_ALARM`.
- **Notifications:** `POST_NOTIFICATIONS` is a normal runtime permission and needs no declaration.
- **Target audience:** choose 13+ (or 18+). Including children under 13 brings in the Families policy and its
  extra requirements.
- **Religious content:** this is allowed. The content rating questionnaire has nothing to declare (no violence,
  user content, gambling or purchases).
