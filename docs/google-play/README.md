# Publishing Salat Desk on Google Play: checklist

## Cost
- **Google Play developer account:** a one-time **US$25** registration fee. There is no yearly fee, and nothing
  else to buy for a free app with no in-app purchases.
- **Optional:** a custom domain or website is not needed. The privacy policy can stay on GitHub.

## 1. Create the developer account (you)
1. Go to <https://play.google.com/console/signup> with the Google account you want to own the app.
2. Choose **Personal** (just you) or **Organization**. An organization needs a D-U-N-S number for a registered
   business.
3. Pay the US$25 with a card that works for international payments.
4. Verify your identity: a government ID, plus a phone number and email you can verify. Approval can take a few
   days.
5. **Personal accounts created after November 2023 have to run a closed test first:** at least **12 testers** must
   stay opted in for **14 days in a row**, then you apply for production access. Start recruiting testers early
   (family, friends, people at the mosque). Each one needs a Google account and an Android phone.

## 2. Build the bundle (you type the key password)

```powershell
powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1 -Format aab
```

This produces `dist\SalatDesk-<version>.aab`. Every upload needs a higher `ApplicationVersion` in
`src/DeskDashboard.App/DeskDashboard.App.csproj`.

## 3. Create the app in Play Console
1. **Create app:**
   - name "Salat Desk: Always-on Prayer";
   - default language English (United States);
   - **App**, **Free**;
   - accept the declarations.
2. **App content:** answer every form with `data-safety.md`.
3. **Store listing:** use `listing.md` and the images in `graphics/`.
4. **App signing:** at the first upload, choose to use your existing key ("Export and upload a key from Java
   keystore"). Play shows the PEPK command to run with `%USERPROFILE%\.salatdesk\salatdesk.keystore`, alias
   `salatdesk`. This keeps the GitHub APK and the Play app on the same signature, so either can update the other.
5. **Testing:**
   1. Testing → Closed testing → create a track.
   2. Add testers by email list or a Google Group.
   3. Upload the `.aab`, roll it out, and share the opt-in link.
6. **After 14 days with 12 or more testers:** Dashboard → Apply for production. Answer the questions about the
   test.
7. **Production:** create a release with the same or a newer bundle and roll it out. Review takes from a few hours
   to a few days.

## 4. Graphics (in `graphics/`)

| Asset | File | Play requirement |
| --- | --- | --- |
| App icon | `icon-512.png` | 512 × 512 PNG, up to 1 MB |
| Feature graphic | `feature-1024x500.png` | 1024 × 500 PNG/JPG, no transparency |
| Phone screenshots | `phone-*.png` | 2–8 images, each side 320–3840 px, long side at most twice the short side |

## After launch
- **Target API:** Google raises the minimum target API level every year, around August. Update .NET when Play
  Console warns about it.
- **Policy updates:** if Play asks you to confirm policy changes, the App content answers usually stay the same.
