'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const js = (name) => path.join(__dirname, '..', '..', 'src', 'DeskDashboard.Web', 'wwwroot', 'js', name);
const ThemeState = require(js('theme-state.js'));
const ContentRef = require(js('content-ref.js'));

test('theme normalize keeps valid values and fills defaults', () => {
  assert.deepEqual(ThemeState.normalize(null), { accent: '#5eead4', font: 'system' });
  assert.deepEqual(ThemeState.normalize({ accent: '#ABC', font: 'mono' }), { accent: '#aabbcc', font: 'mono' });
  assert.deepEqual(ThemeState.normalize({ accent: 'red', font: 'comic' }), { accent: '#5eead4', font: 'system' });
});

test('palette derives every shade from the accent', () => {
  const p = ThemeState.palette('#5eead4');
  assert.equal(p['--accent'], '#5eead4');
  assert.equal(p['--accent-rgb'], '94, 234, 212');
  for (const key of ['--accent-hover', '--accent-ink', '--accent-bg', '--ambient-accent']) {
    assert.match(p[key], /^#[0-9a-f]{6}$/);
  }
  // Ink is dark enough to read on the accent button.
  assert.ok(parseInt(p['--accent-ink'].slice(1, 3), 16) < 40);
});

test('every font has a family and a weight', () => {
  for (const font of ThemeState.FONTS) {
    assert.ok(font.family && font.weight);
  }
  assert.equal(ThemeState.fontFor('nope').key, 'system');
});

test('Quran references', () => {
  assert.equal(ContentRef.format({ type: 'quran', key: 'quran:9:10' }), 'Quran (9:10)');
  assert.equal(ContentRef.format({ type: 'quran', key: 'quran:94:5-8' }), 'Quran (94:5-8)');
});

test('Hadith references use book/number when known', () => {
  assert.equal(ContentRef.format({ type: 'hadith', key: 'bukhari:8', refBook: 2, refNumber: 1 }), 'Hadis (Bukhari: 2/1)');
  assert.equal(ContentRef.format({ type: 'hadith', key: 'muslim:2609a', refBook: 45, refNumber: 134 }), 'Hadis (Muslim: 45/134)');
  assert.equal(ContentRef.format({ type: 'hadith', key: 'nawawi:7', refBook: 1, refNumber: 7 }), 'Hadis (Nawawi 40: 7)');
  assert.equal(ContentRef.format({ type: 'hadith', key: 'qudsi:5', refBook: 1, refNumber: 5 }), 'Hadis (Qudsi 40: 5)');
});

test('Hadith without a book number falls back to the collection number', () => {
  assert.equal(ContentRef.format({ type: 'hadith', key: 'bukhari:6168' }), 'Hadis (Bukhari: 6168)');
  assert.equal(ContentRef.format(null), '');
});
