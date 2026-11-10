'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const AmbientState = require(path.join(
  __dirname, '..', '..', 'src', 'DeskDashboard.Web', 'wwwroot', 'js', 'ambient-state.js'));

const MINUTE = 60 * 1000;
// Dhaka is UTC+6 with no DST, which keeps the local-minute helper simple.
const OFFSET = 6 * 60;
const minutesOfDay = (ms) => (((Math.floor(ms / MINUTE) + OFFSET) % 1440) + 1440) % 1440;
const at = (local) => Date.parse(`${local}+06:00`);

function entries({ asrJamaat = '2026-11-10T16:30:00', fajrValid = true } = {}) {
  return [
    { key: 'isha', isPrayer: true, at: '2026-11-09T18:32:00+06:00', jamaatTime: '20:00', jamaatAt: '2026-11-09T20:00:00+06:00' },
    { key: 'fajr', isPrayer: true, at: '2026-11-10T04:52:00+06:00', jamaatTime: '05:30', jamaatAt: '2026-11-10T05:30:00+06:00', jamaatWithinWindow: fajrValid },
    { key: 'sunrise', isPrayer: false, at: '2026-11-10T06:09:00+06:00' },
    { key: 'dhuhr', isPrayer: true, at: '2026-11-10T11:43:00+06:00', jamaatTime: null, jamaatAt: null },
    { key: 'asr', isPrayer: true, at: '2026-11-10T15:39:00+06:00', jamaatTime: asrJamaat ? '16:30' : null, jamaatAt: asrJamaat ? `${asrJamaat}+06:00` : null },
    { key: 'maghrib', isPrayer: true, at: '2026-11-10T17:15:00+06:00', jamaatTime: '18:00', jamaatAt: '2026-11-10T18:00:00+06:00' },
    { key: 'isha', isPrayer: true, at: '2026-11-10T18:32:00+06:00', jamaatTime: '20:00', jamaatAt: '2026-11-10T20:00:00+06:00' },
    { key: 'fajr', isPrayer: true, at: '2026-11-11T04:53:00+06:00', jamaatTime: '05:30', jamaatAt: '2026-11-11T05:30:00+06:00' }
  ];
}

const prefs = AmbientState.normalizePrefs({});

function mode(local, overrides = {}) {
  return AmbientState.decideMode({
    now: at(local),
    prefs: overrides.prefs || prefs,
    entries: overrides.entries || entries(),
    phase: overrides.phase || null,
    wakeUntil: overrides.wakeUntil || 0,
    forceFocus: overrides.forceFocus || false,
    aod: overrides.aod || null,
    minutesOfDay
  });
}

test('defaults are sane and invalid prefs are clamped or replaced', () => {
  assert.equal(prefs.enabled, true);
  assert.equal(prefs.focusBeforeMinutes, 15);
  assert.equal(prefs.focusAfterMinutes, 10);
  assert.equal(prefs.nightStart, '23:00');

  const odd = AmbientState.normalizePrefs({
    focusBeforeMinutes: 500, focusAfterMinutes: -3, nightStart: '25:99', ambientBrightness: 9, nightBrightness: 0
  }, { enabled: false });
  assert.equal(odd.enabled, false);
  assert.equal(odd.focusBeforeMinutes, 60);
  assert.equal(odd.focusAfterMinutes, 0);
  assert.equal(odd.nightStart, '23:00');
  assert.equal(odd.ambientBrightness, 1);
  assert.equal(odd.nightBrightness, 0.01);
});

test('ambient between prayers, focus around Jamaat with end-exclusive window', () => {
  assert.equal(mode('2026-11-10T14:00:00').mode, 'ambient');
  assert.equal(mode('2026-11-10T16:14:59').mode, 'ambient');
  assert.equal(mode('2026-11-10T16:15:00').mode, 'focus');
  assert.equal(mode('2026-11-10T16:30:00').mode, 'focus');
  assert.equal(mode('2026-11-10T16:39:59').mode, 'focus');
  assert.equal(mode('2026-11-10T16:40:00').mode, 'ambient');
});

test('without a Jamaat the focus window is anchored on the calculated Waqt', () => {
  // Dhuhr has no Jamaat: 11:43 ± window.
  assert.equal(mode('2026-11-10T11:27:59').mode, 'ambient');
  assert.equal(mode('2026-11-10T11:28:00').mode, 'focus');
  assert.equal(mode('2026-11-10T11:52:59').mode, 'focus');
  assert.equal(mode('2026-11-10T11:53:00').mode, 'ambient');
});

test('an out-of-window Jamaat falls back to the Waqt anchor', () => {
  const list = entries({ fajrValid: false });
  assert.equal(mode('2026-11-10T04:40:00', { entries: list, prefs: { ...prefs, nightEnabled: false } }).mode, 'focus');
  assert.equal(mode('2026-11-10T05:25:00', { entries: list }).mode, 'ambient');
});

test('approaching or reached Jamaat always forces focus', () => {
  const tight = { ...prefs, focusBeforeMinutes: 0, focusAfterMinutes: 0 };
  assert.equal(mode('2026-11-10T16:25:00', { prefs: tight }).mode, 'ambient');
  assert.equal(mode('2026-11-10T16:25:00', { prefs: tight, phase: 'approaching' }).mode, 'focus');
  assert.equal(mode('2026-11-10T16:30:20', { prefs: tight, phase: 'reached' }).mode, 'focus');
});

test('wake, settings and disabled ambient all keep the full view', () => {
  const now = at('2026-11-10T14:00:00');
  assert.equal(mode('2026-11-10T14:00:00', { wakeUntil: now + MINUTE }).reason, 'wake');
  assert.equal(mode('2026-11-10T14:00:00', { wakeUntil: now - 1 }).mode, 'ambient');
  assert.equal(mode('2026-11-10T14:00:00', { forceFocus: true }).reason, 'forced');
  assert.equal(mode('2026-11-10T14:00:00', { prefs: { ...prefs, enabled: false } }).reason, 'disabled');
});

test('night runs from the night start until the next Fajr Waqt, across midnight', () => {
  assert.equal(mode('2026-11-10T22:59:59').mode, 'ambient');
  assert.equal(mode('2026-11-10T23:00:00').mode, 'night');
  assert.equal(mode('2026-11-11T02:00:00').mode, 'night');
  // Early morning of the 10th, before that day's Fajr (night began 23:00 on the 9th).
  assert.equal(mode('2026-11-10T03:00:00').mode, 'night');
  // 15 minutes before Fajr Jamaat (05:30) is focus; between Fajr Waqt and then is ambient.
  assert.equal(mode('2026-11-10T04:52:00').mode, 'ambient');
  assert.equal(mode('2026-11-10T05:15:00').mode, 'focus');
});

test('focus beats night when a Jamaat window falls inside the night', () => {
  const late = entries();
  late[6] = { ...late[6], jamaatTime: '23:30', jamaatAt: '2026-11-10T23:30:00+06:00' };
  assert.equal(mode('2026-11-10T23:20:00', { entries: late }).mode, 'focus');
  assert.equal(mode('2026-11-10T23:45:00', { entries: late }).mode, 'night');
});

test('night can be disabled, and a custom start time is respected', () => {
  assert.equal(mode('2026-11-11T01:00:00', { prefs: { ...prefs, nightEnabled: false } }).mode, 'ambient');
  assert.equal(mode('2026-11-10T21:30:00', { prefs: { ...prefs, nightStart: '21:00' } }).mode, 'night');
  assert.equal(mode('2026-11-10T20:59:00', { prefs: { ...prefs, nightStart: '21:00' } }).mode, 'ambient');
  // A night start after midnight still ends at Fajr.
  assert.equal(mode('2026-11-11T00:30:00', { prefs: { ...prefs, nightStart: '01:00' } }).mode, 'ambient');
  assert.equal(mode('2026-11-11T01:30:00', { prefs: { ...prefs, nightStart: '01:00' } }).mode, 'night');
});

test('block position changes every minute, stays in range and spreads out', () => {
  const base = at('2026-11-10T14:00:00');
  const positions = [];
  for (let i = 0; i < 60; i += 1) {
    const p = AmbientState.blockPosition(base + i * MINUTE + 30 * 1000);
    assert.ok(p.fx >= 0 && p.fx < 1 && p.fy >= 0 && p.fy < 1);
    positions.push(p);
  }
  for (let i = 1; i < positions.length; i += 1) {
    assert.notEqual(positions[i].slot, positions[i - 1].slot);
    const moved = Math.hypot(positions[i].fx - positions[i - 1].fx, positions[i].fy - positions[i - 1].fy);
    assert.ok(moved > 0.1, `minute ${i} moved only ${moved}`);
  }
  // Every quadrant of the free space is used within an hour.
  const quadrants = new Set(positions.map((p) => `${p.fx < 0.5 ? 'L' : 'R'}${p.fy < 0.5 ? 'T' : 'B'}`));
  assert.equal(quadrants.size, 4);
  // Same minute, same place.
  assert.deepEqual(AmbientState.blockPosition(base + 1000), AmbientState.blockPosition(base + 59 * 1000));
});

test('brightness per mode: system in focus, dim in ambient, dimmest at night', () => {
  assert.equal(AmbientState.brightnessFor('focus', prefs), -1);
  assert.equal(AmbientState.brightnessFor('ambient', prefs), prefs.ambientBrightness);
  assert.equal(AmbientState.brightnessFor('night', prefs), prefs.nightBrightness);
  assert.ok(prefs.nightBrightness < prefs.ambientBrightness);
});

test('content choice is normalized', () => {
  assert.equal(AmbientState.normalizePrefs({}).content, 'both');
  assert.equal(AmbientState.normalizePrefs({ content: 'hadith' }).content, 'hadith');
  assert.equal(AmbientState.normalizePrefs({ content: 'nonsense' }).content, 'both');
});

test('typeForSlot alternates and falls back to the kind that exists', () => {
  assert.equal(AmbientState.typeForSlot(10, 'both', true, true), 'quran');
  assert.equal(AmbientState.typeForSlot(11, 'both', true, true), 'hadith');
  assert.equal(AmbientState.typeForSlot(11, 'both', true, false), 'quran');
  assert.equal(AmbientState.typeForSlot(10, 'hadith', true, true), 'hadith');
  assert.equal(AmbientState.typeForSlot(10, 'quran', false, true), 'hadith');
  assert.equal(AmbientState.typeForSlot(10, 'off', true, true), null);
  assert.equal(AmbientState.typeForSlot(10, 'both', false, false), null);
});

test('chooseNext shows never-shown items first, then the least recently shown', () => {
  const items = [
    { id: 1, type: 'quran', lastShownAt: 5000 },
    { id: 2, type: 'quran', lastShownAt: 1000 },
    { id: 3, type: 'hadith', lastShownAt: null },
    { id: 4, type: 'quran', lastShownAt: null },
    { id: 5, type: 'quran', lastShownAt: null }
  ];
  assert.equal(AmbientState.chooseNext(items, 'quran').id, 4);
  items[3].lastShownAt = 9000;
  assert.equal(AmbientState.chooseNext(items, 'quran').id, 5);
  items[4].lastShownAt = 9500;
  assert.equal(AmbientState.chooseNext(items, 'quran').id, 2);
  assert.equal(AmbientState.chooseNext(items, 'hadith').id, 3);
});

test('chooseNext skips removed items and items rejected by skip (too long)', () => {
  const items = [
    { id: 1, type: 'quran', lastShownAt: null, hidden: true },
    { id: 2, type: 'quran', lastShownAt: null },
    { id: 3, type: 'quran', lastShownAt: 100 }
  ];
  assert.equal(AmbientState.chooseNext(items, 'quran').id, 2);
  assert.equal(AmbientState.chooseNext(items, 'quran', (item) => item.id === 2).id, 3);
  assert.equal(AmbientState.chooseNext(items, 'quran', () => true), null);
  assert.equal(AmbientState.chooseNext([], 'quran'), null);
});

test('rotating through the list shows every item once before repeating', () => {
  const items = Array.from({ length: 7 }, (_, i) => ({ id: i + 1, type: 'hadith', lastShownAt: null }));
  const shown = [];
  for (let minute = 0; minute < 14; minute += 1) {
    const next = AmbientState.chooseNext(items, 'hadith');
    next.lastShownAt = minute;
    shown.push(next.id);
  }
  assert.equal(new Set(shown.slice(0, 7)).size, 7);
  assert.deepEqual(shown.slice(7), shown.slice(0, 7));
});

test('AOD skips the current focus window, then the next prayer window ends it', () => {
  const now = at('2026-11-10T16:20:00'); // inside Asr window (Jamaat 16:30)
  const window = AmbientState.activeWindow(now, entries(), prefs);
  assert.equal(window.key, 'asr');
  const aod = { skipUntil: window.end };

  assert.equal(mode('2026-11-10T16:20:00', { aod }).mode, 'ambient');
  assert.equal(mode('2026-11-10T16:30:00', { aod, phase: 'reached' }).mode, 'ambient');
  // Between windows AOD keeps ambient.
  assert.equal(mode('2026-11-10T17:00:00', { aod }).reason, 'aod');
  // Maghrib window (Jamaat 18:00) starts at 17:45: AOD ends.
  const maghrib = mode('2026-11-10T17:45:00', { aod });
  assert.equal(maghrib.mode, 'focus');
  assert.equal(maghrib.aodEnded, true);
});

test('AOD works with ambient mode disabled, and tap or settings still win', () => {
  const off = { ...prefs, enabled: false };
  const aod = { skipUntil: at('2026-11-10T14:00:00') };
  assert.equal(mode('2026-11-10T14:00:00', { prefs: off }).mode, 'focus');
  assert.equal(mode('2026-11-10T14:00:00', { prefs: off, aod }).mode, 'ambient');
  const now = at('2026-11-10T14:00:00');
  assert.equal(mode('2026-11-10T14:00:00', { aod, wakeUntil: now + MINUTE }).mode, 'focus');
  assert.equal(mode('2026-11-10T14:00:00', { aod, forceFocus: true }).mode, 'focus');
  // At night AOD shows the night view.
  assert.equal(mode('2026-11-10T23:30:00', { aod }).mode, 'night');
});
