# Answers for Play Console "App content" forms

Play Console → Policy and programs → App content. These answers match the app as it is now (version 0.1.0). If you
add a feature that sends data anywhere, update them and `PRIVACY.md`.

## Privacy policy
`https://github.com/fahad-poran/salat-desk/blob/main/PRIVACY.md`

## Ads
**No**, the app does not contain ads.

## App access
**All functionality is available without special access.** There is no login.

## Content rating (IARC questionnaire)
- Category: **Reference, News, or Educational**. Choose "All other app types" if that is the only fitting option.
- Violence, sexuality, language, controlled substances, gambling: **No** to all.
- Users can interact or share content with each other: **No**.
- Shares the user's location with other users: **No**.
- Digital purchases: **No**.

Expected rating: Everyone / PEGI 3.

## Target audience and content
- Target age groups: **13–15, 16–17 and 18+**. Leave out under-13 groups; including them brings in the Families
  policy.
- Appeals to children: **No**.

## Data safety

| Question | Answer |
| --- | --- |
| Does your app collect or share any of the required user data types? | **Yes** (only the city search, below) |
| Is all user data encrypted in transit? | **Yes** (HTTPS only) |
| Do you provide a way for users to request deletion? | No account and nothing stored off the phone. Uninstalling deletes everything. Answer per the form's options. |

The only data that leaves the phone is the text typed into the city search. It is sent to Open-Meteo to find the
place and isn't stored by the app's developer.

| Data type | Collected | Shared | Ephemeral | Required or optional | Purpose |
| --- | --- | --- | --- | --- | --- |
| App activity → **In-app search history** | Yes | No (Open-Meteo is a service provider acting for the app) | **Yes** | Optional (you can type coordinates instead) | App functionality |

Everything else is **not collected**:
- location from GPS or the network (the app never reads it);
- personal info, financial info, messages, photos, contacts, health;
- device IDs, crash logs and diagnostics.

The weekly Quran/Hadith download sends no user data. It only requests public files.

> Some developers answer "No data collected" because the search is user-initiated and ephemeral. The answer above
> is the cautious one that matches Google's definition of collection (data that leaves the device).

## Government app, financial features, health, news
**No** to each.

## Permissions declarations
None needed:
- `SCHEDULE_EXACT_ALARM` doesn't need a declaration (only `USE_EXACT_ALARM` does, which the app doesn't use).
- No foreground service, background location, SMS or call-log permissions.
