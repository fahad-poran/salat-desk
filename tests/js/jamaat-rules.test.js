'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const JamaatRules = require(path.join(__dirname, '..', '..', 'src', 'DeskDashboard.Web', 'wwwroot', 'js', 'jamaat-rules.js'));

const asr = JamaatRules.windowFrom('16:08', '17:20'); // Asr 4:08 PM, last minute before Maghrib 5:20 PM
const isha = JamaatRules.windowFrom('18:45', '04:49'); // passes midnight

test('missing rule is fixed with a Waqt + 10 fallback', () => {
  assert.deepEqual(JamaatRules.normalizeRule(null), { mode: 'fixed', fallback: { mode: 'offset', minutes: 10 } });
});

test('bad values are clamped or replaced', () => {
  assert.deepEqual(JamaatRules.normalizeRule({ mode: 'slot', slot: 7, gap: 99 }), { mode: 'slot', slot: 15, gap: 60 });
  assert.deepEqual(JamaatRules.normalizeRule({ mode: 'offset', minutes: -4 }), { mode: 'offset', minutes: 0 });
  assert.equal(JamaatRules.normalizeRule({ mode: 'nope' }).mode, 'fixed');
});

test('fixed time inside the window is used as is', () => {
  const result = JamaatRules.resolve({ mode: 'fixed' }, '16:30', asr);
  assert.equal(result.at, 16 * 60 + 30);
  assert.equal(result.valid, true);
  assert.equal(result.adjusted, false);
});

test('fixed time before the Waqt falls back to Waqt + N', () => {
  const result = JamaatRules.resolve({ mode: 'fixed', fallback: { mode: 'offset', minutes: 10 } }, '16:00', asr);
  assert.equal(JamaatRules.formatTime(result.at), '4:18 PM');
  assert.equal(result.adjusted, true);
});

test('fixed time before the Waqt falls back to the next slot', () => {
  const result = JamaatRules.resolve({ mode: 'fixed', fallback: { mode: 'slot', slot: 15, gap: 5 } }, '16:00', asr);
  assert.equal(JamaatRules.formatTime(result.at), '4:15 PM');
});

test('warn keeps the fixed time and marks it invalid', () => {
  const result = JamaatRules.resolve({ mode: 'fixed', fallback: { mode: 'warn' } }, '16:00', asr);
  assert.equal(result.at, 16 * 60);
  assert.equal(result.valid, false);
});

test('fixed time after the window end also falls back', () => {
  const result = JamaatRules.resolve({ mode: 'fixed', fallback: { mode: 'offset', minutes: 5 } }, '17:30', asr);
  assert.equal(JamaatRules.formatTime(result.at), '4:13 PM');
});

test('offset adds minutes to the Waqt and is capped at the window', () => {
  assert.equal(JamaatRules.formatTime(JamaatRules.resolve({ mode: 'offset', minutes: 5 }, null, asr).at), '4:13 PM');
  const capped = JamaatRules.resolve({ mode: 'offset', minutes: 120 }, null, asr);
  assert.equal(JamaatRules.formatTime(capped.at), '5:20 PM');
  assert.equal(capped.capped, true);
});

test('slot rounding for several slot sizes and gaps', () => {
  const at = (slot, gap) => JamaatRules.formatTime(JamaatRules.resolve({ mode: 'slot', slot, gap }, null, asr).at);
  assert.equal(at(15, 5), '4:15 PM'); // 4:13 -> 4:15
  assert.equal(at(10, 0), '4:10 PM'); // 4:08 -> 4:10
  assert.equal(at(30, 10), '4:30 PM'); // 4:18 -> 4:30
  assert.equal(at(15, 7), '4:15 PM'); // exactly on a slot
  assert.equal(at(15, 8), '4:30 PM'); // 4:16 -> 4:30
});

test('Isha window past midnight', () => {
  assert.equal(JamaatRules.resolve({ mode: 'fixed' }, '00:30', isha).valid, true);
  assert.equal(JamaatRules.formatTime(JamaatRules.resolve({ mode: 'slot', slot: 30, gap: 10 }, null, isha).at), '7:00 PM');
});

test('no Jamaat without a fixed time', () => {
  assert.equal(JamaatRules.resolve({ mode: 'fixed' }, '', asr), null);
});
