# Salat Desk privacy policy

_Last updated: 10 November 2026_

Salat Desk is a prayer-time clock for Android phones. It is built to work without collecting anything about you.

## What stays on your phone

Everything you enter or that the app creates is stored only on your phone, in the app's private storage:
- your location (city, coordinates, time zone);
- your calculation settings and mosque Jamaat timetable;
- your alert and display preferences;
- the Quran and Hadith texts, and which ones you removed or have been shown.

Nothing is sent to the developer. There is no account, no analytics, no advertising and no crash reporting.
Uninstalling the app deletes all of it.

## When the app connects to the internet

| What | Service | What is sent |
| --- | --- | --- |
| City search in Settings (only when you search) | Open-Meteo geocoding API (open-meteo.com) | The text you type in the search box |
| Weekly download of new Quran verses | AlQuran.cloud API (alquran.cloud) | Requests for specific verse references |
| Weekly download of new hadith | hadith-api files on jsDelivr (cdn.jsdelivr.net) | Requests for public hadith collection files |

These requests contain no personal information beyond what any internet request reveals, such as your IP address,
which those services may log under their own policies. All of them use encrypted HTTPS connections. Prayer times
are calculated on the phone, without any internet connection.

## Notifications

Jamaat notifications are created and scheduled by the phone itself. No push service or server is involved.

## Permissions

| Permission | Why |
| --- | --- |
| Internet, network state | The city search and the weekly content download (the download waits for Wi-Fi) |
| Notifications | Jamaat warnings, if you turn them on |
| Exact alarms | Showing the Jamaat warning at the exact minute |
| Vibration | The Jamaat warning |

The app does not access your contacts, photos, camera, microphone or GPS location.

## Children

Salat Desk is not directed at children under 13 and does not knowingly collect any data from anyone.

## Changes and contact

Any change to this policy will be published on this page. For questions, open an issue at
<https://github.com/fahad-poran/salat-desk/issues>.
