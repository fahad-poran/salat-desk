'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const JamaatState = require(path.join(
  __dirname,
  '..',
  '..',
  'src',
  'DeskDashboard.Web',
  'wwwroot',
  'js',
  'jamaat-state.js'
));

const MINUTE = 60 * 1000;
const BASE = Date.parse('2026-11-04T00:00:00+06:00');

function iso(dayOffset, hour, minute) {
  return new Date(BASE + ((dayOffset * 24 + hour) * 60 + minute) * MINUTE).toISOString();
}

function prayer(key, name, dayOffset, hour, minute, jamaatHour, jamaatMinute, extra = {}) {
  const hasJamaat = jamaatHour !== null && jamaatHour !== undefined;
  return {
    key,
    name,
    isPrayer: true,
    at: iso(dayOffset, hour, minute),
    jamaatTime: hasJamaat ? `${String(jamaatHour).padStart(2, '0')}:${String(jamaatMinute).padStart(2, '0')}` : null,
    jamaatAt: hasJamaat ? iso(dayOffset, jamaatHour, jamaatMinute) : null,
    jamaatWithinWindow: hasJamaat ? true : null,
    jamaatValidationError: null,
    ...extra
  };
}

function todayEntries(options = {}) {
  const dhuhrJamaat = options.dhuhrJamaat === undefined ? [12, 15] : options.dhuhrJamaat;
  const dhuhrExtra = {};
  if (options.dhuhrWithinWindow === false) {
    dhuhrExtra.jamaatWithinWindow = false;
  }
  if (options.dhuhrError) {
    dhuhrExtra.jamaatValidationError = options.dhuhrError;
  }

  return [
    prayer('fajr', 'Fajr', 0, 4, 49, 5, 30),
    prayer('sunrise', 'Sunrise', 0, 6, 6, null, null, { isPrayer: false }),
    prayer('dhuhr', 'Dhuhr', 0, 11, 43, ...(dhuhrJamaat || [null, null]), dhuhrExtra),
    prayer('asr', 'Asr', 0, 15, 41, 16, 10),
    prayer('maghrib', 'Maghrib', 0, 17, 18, 17, 45),
    prayer('isha', 'Isha', 0, 18, 35, 19, 45)
  ];
}

function tomorrowFajr() {
  return prayer('fajr', 'Fajr', 1, 4, 49, 5, 15);
}

test('before Waqt reports upcoming and counts to the start', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (11 * 60 + 30) * MINUTE);

  assert.equal(view.phase, 'upcoming');
  assert.equal(view.prayer.key, 'dhuhr');
  assert.equal(view.targetKind, 'waqt');
  assert.equal(view.remainingMs, 13 * MINUTE);
  assert.equal(view.warning, false);
});

test('immediately after Waqt reports waiting for Jamaat', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (11 * 60 + 44) * MINUTE);

  assert.equal(view.phase, 'waiting');
  assert.equal(view.prayer.key, 'dhuhr');
  assert.equal(view.targetKind, 'jamaat');
  assert.equal(view.remainingMs, 31 * MINUTE);
  assert.equal(view.warning, false);
});

test('more than ten minutes before Jamaat stays waiting', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (11 * 60 + 50) * MINUTE);

  assert.equal(view.phase, 'waiting');
  assert.equal(view.remainingMs, 25 * MINUTE);
  assert.equal(view.warning, false);
});

test('exactly ten minutes before Jamaat activates the warning', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (12 * 60 + 5) * MINUTE);

  assert.equal(view.phase, 'approaching');
  assert.equal(view.remainingMs, 10 * MINUTE);
  assert.equal(view.warning, true);
});

test('less than ten minutes before Jamaat keeps the warning active', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (12 * 60 + 6) * MINUTE);

  assert.equal(view.phase, 'approaching');
  assert.equal(view.remainingMs, 9 * MINUTE);
  assert.equal(view.warning, true);
});

test('exactly at Jamaat reports reached', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (12 * 60 + 15) * MINUTE);

  assert.equal(view.phase, 'reached');
  assert.equal(view.warning, false);
  assert.equal(view.targetKind, 'jamaat');
});

test('after Jamaat reports passed within the grace period', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (12 * 60 + 15) * MINUTE + 30 * 1000);

  assert.equal(view.phase, 'reached');

  const passed = JamaatState.evaluate(todayEntries(), BASE + (12 * 60 + 16) * MINUTE);
  assert.equal(passed.phase, 'passed');
  assert.equal(passed.warning, false);

  const stillPassed = JamaatState.evaluate(todayEntries(), BASE + (12 * 60 + 19) * MINUTE + 30 * 1000);
  assert.equal(stillPassed.phase, 'passed');
});

test('after the passed grace period moves to the next prayer', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (12 * 60 + 21) * MINUTE);

  assert.equal(view.phase, 'upcoming');
  assert.equal(view.prayer.key, 'asr');
  assert.equal(view.targetKind, 'waqt');
});

test('missing Jamaat reports missing until the next Waqt', () => {
  const view = JamaatState.evaluate(
    todayEntries({ dhuhrJamaat: null }),
    BASE + (11 * 60 + 50) * MINUTE
  );

  assert.equal(view.phase, 'missing');
  assert.equal(view.prayer.key, 'dhuhr');
  assert.equal(view.prayer.jamaatTime, null);
  assert.equal(view.targetKind, 'next');
  assert.equal(view.remainingMs, (15 * 60 + 41 - (11 * 60 + 50)) * MINUTE);
});

test('missing Jamaat hands over when the next prayer starts', () => {
  const view = JamaatState.evaluate(
    todayEntries({ dhuhrJamaat: null }),
    BASE + (15 * 60 + 42) * MINUTE
  );

  assert.equal(view.phase, 'waiting');
  assert.equal(view.prayer.key, 'asr');
});

test('missing Jamaat before Waqt is still upcoming', () => {
  const view = JamaatState.evaluate(
    todayEntries({ dhuhrJamaat: null }),
    BASE + (11 * 60 + 30) * MINUTE
  );

  assert.equal(view.phase, 'upcoming');
  assert.equal(view.prayer.key, 'dhuhr');
});

test('invalid Jamaat is flagged and does not count down', () => {
  const view = JamaatState.evaluate(
    todayEntries({ dhuhrJamaat: [11, 30], dhuhrWithinWindow: false, dhuhrError: 'Before Dhuhr start.' }),
    BASE + (11 * 60 + 50) * MINUTE
  );

  assert.equal(view.phase, 'invalid');
  assert.equal(view.prayer.key, 'dhuhr');
  assert.equal(view.prayer.jamaatAt, null);
  assert.equal(view.prayer.validationError, 'Before Dhuhr start.');
  assert.equal(view.targetKind, 'next');
});

test('Isha Jamaat after midnight is handled across the day transition', () => {
  const previousIsha = prayer('isha', 'Isha', -1, 18, 35, 0, 30);
  previousIsha.jamaatAt = iso(0, 0, 30);
  const entries = [
    previousIsha,
    ...todayEntries(),
    tomorrowFajr()
  ];

  const waiting = JamaatState.evaluate(entries, BASE + 10 * MINUTE);
  assert.equal(waiting.phase, 'waiting');
  assert.equal(waiting.prayer.key, 'isha');
  assert.equal(waiting.remainingMs, 20 * MINUTE);

  const passed = JamaatState.evaluate(entries, BASE + 33 * MINUTE);
  assert.equal(passed.phase, 'passed');
  assert.equal(passed.prayer.key, 'isha');

  const next = JamaatState.evaluate(entries, BASE + 36 * MINUTE);
  assert.equal(next.phase, 'upcoming');
  assert.equal(next.prayer.key, 'fajr');
  assert.equal(next.prayer.at, BASE + (4 * 60 + 49) * MINUTE);
});

test('sunrise is never treated as a prayer', () => {
  const view = JamaatState.evaluate(todayEntries(), BASE + (6 * 60 + 10) * MINUTE);

  assert.equal(view.prayer.key, 'dhuhr');
  assert.notEqual(view.phase, 'missing');
});

test('re-evaluating during the warning is deterministic and keeps the event key', () => {
  const entries = todayEntries();
  const now = BASE + (12 * 60 + 7) * MINUTE;

  const first = JamaatState.evaluate(entries, now);
  const afterRefresh = JamaatState.evaluate(entries, now);

  assert.equal(first.phase, 'approaching');
  assert.equal(afterRefresh.phase, 'approaching');
  assert.equal(first.eventKey, afterRefresh.eventKey);
  assert.equal(first.eventKey, first.prayer.key + '|' + first.prayer.jamaatAt);
});

test('timestamps may be supplied as ISO strings', () => {
  const view = JamaatState.evaluate(todayEntries(), new Date(BASE + (12 * 60 + 5) * MINUTE).toISOString());

  assert.equal(view.phase, 'approaching');
  assert.equal(view.remainingMs, 10 * MINUTE);
});

test('empty or malformed entries return idle', () => {
  assert.equal(JamaatState.evaluate([], BASE).phase, 'idle');
  assert.equal(JamaatState.evaluate([{ key: 'dhuhr', at: 'not-a-date' }], BASE).phase, 'idle');
  assert.equal(JamaatState.evaluate(null, BASE).phase, 'idle');
});

test('splitDuration formats countdown values', () => {
  assert.deepEqual(
    JamaatState.splitDuration(25 * MINUTE + 4000),
    { hours: 0, minutes: 25, seconds: 4, totalMinutes: 26, clock: '00:25:04' }
  );

  assert.deepEqual(
    JamaatState.splitDuration(3661 * 1000),
    { hours: 1, minutes: 1, seconds: 1, totalMinutes: 62, clock: '01:01:01' }
  );
});

test('warning minutes are per prayer and limited to the Waqt-to-Jamaat time', () => {
  const entries = todayEntries();
  const at = (h, m) => Date.parse(iso(0, h, m));
  // Asr Waqt 15:41, Jamaat 16:10 (29 minutes apart).
  assert.equal(JamaatState.evaluate(entries, at(15, 55)).phase, 'waiting');
  const custom = { warningMinutes: { asr: 20 } };
  const early = JamaatState.evaluate(entries, at(15, 55), custom);
  assert.equal(early.phase, 'approaching');
  assert.equal(early.warningMs, 20 * MINUTE);
  assert.equal(JamaatState.evaluate(entries, at(15, 49), custom).phase, 'waiting');
  // 90 minutes asked, only 29 available: the whole Waqt-to-Jamaat time is the warning.
  const long = JamaatState.evaluate(entries, at(15, 42), { warningMinutes: { asr: 90 } });
  assert.equal(long.phase, 'approaching');
  assert.equal(long.warningMs, 29 * MINUTE);
  // Other prayers keep the default.
  assert.equal(JamaatState.evaluate(entries, at(19, 30), custom).warningMs, 10 * MINUTE);
});

test('alert preferences are normalised for all five prayers', () => {
  const prefs = JamaatState.normalizeAlertPrefs({ asr: { notify: false, minutes: 25 }, isha: { minutes: 999 }, fajr: { minutes: 'x' } });
  assert.deepEqual(Object.keys(prefs), ['fajr', 'dhuhr', 'asr', 'maghrib', 'isha']);
  assert.deepEqual(prefs.asr, { notify: false, minutes: 25 });
  assert.equal(prefs.isha.minutes, 120);
  assert.equal(prefs.fajr.minutes, 10);
  assert.equal(prefs.dhuhr.notify, true);
  assert.deepEqual(JamaatState.warningMinutesOf(prefs).asr, 25);
});

test('alert schedule lists upcoming warnings for prayers with Notify on', () => {
  const entries = [...todayEntries(), tomorrowFajr()];
  const prefs = JamaatState.normalizeAlertPrefs({ maghrib: { notify: false }, asr: { minutes: 15 } });
  const now = Date.parse(iso(0, 15, 0));
  const schedule = JamaatState.alertSchedule(entries, prefs, now);
  assert.deepEqual(schedule.map((s) => s.key), ['asr', 'isha', 'fajr']);
  const asr = schedule[0];
  assert.equal(asr.warnAt, Date.parse(iso(0, 16, 10)) - 15 * MINUTE);
  assert.equal(asr.minutes, 15);
  assert.equal(asr.id, `asr|${Date.parse(iso(0, 16, 10))}`);
  // Once a warning time has passed it is no longer scheduled.
  assert.deepEqual(JamaatState.alertSchedule(entries, prefs, Date.parse(iso(0, 15, 56))).map((s) => s.key), ['isha', 'fajr']);
  // Prayers without a Jamaat are skipped.
  const noDhuhr = JamaatState.alertSchedule(todayEntries({ dhuhrJamaat: null }), prefs, Date.parse(iso(0, 8, 0)));
  assert.ok(!noDhuhr.some((s) => s.key === 'dhuhr'));
});
