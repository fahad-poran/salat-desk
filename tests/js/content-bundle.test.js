'use strict';

// Checks the generated offline Ayah/Hadith bundle (tools/content/build-content.mjs).
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');

const bundle = require(path.join(
  __dirname, '..', '..', 'src', 'DeskDashboard.Core', 'Content', 'seed-content.json'));

const ARABIC = /[؀-ۿ]/;
const LATIN = /[A-Za-z]/;

test('bundle has both kinds of content and credits its sources', () => {
  assert.ok(bundle.quran.length >= 50, `quran: ${bundle.quran.length}`);
  assert.ok(bundle.hadith.length >= 100, `hadith: ${bundle.hadith.length}`);
  const credits = bundle.credits.join(' ');
  for (const name of ['Tanzil', 'Sahih International', 'hadith-api']) {
    assert.ok(credits.includes(name), `credit for ${name}`);
  }
});

test('every item has Arabic, English and a reference within the display limits', () => {
  for (const item of [...bundle.quran, ...bundle.hadith]) {
    assert.ok(ARABIC.test(item.ar) && !LATIN.test(item.ar), `Arabic text for ${item.ref}`);
    assert.ok(LATIN.test(item.en), `English text for ${item.ref}`);
    assert.ok(item.ref && /\d/.test(item.ref), `reference for ${item.ref}`);
    assert.ok(item.ar.length <= 420 && item.en.length <= 400, `length of ${item.ref}`);
    assert.match(item.key, /^(quran|bukhari|muslim|nawawi|qudsi):/, `key of ${item.ref}`);
    assert.equal(item.ar, item.ar.trim());
  }
});

test('Quran references are well-formed and the Basmala is not prefixed to ayah 1', () => {
  for (const item of bundle.quran) {
    assert.equal(item.type, 'quran');
    assert.match(item.ref, /^\S.* \d{1,3}:\d{1,3}(–\d{1,3})?$/);
    const [, surah, first] = /(\d+):(\d+)/.exec(item.ref);
    if (first === '1' && surah !== '1') {
      assert.ok(!item.ar.startsWith('بِسْمِ'), `Basmala on ${item.ref}`);
    }
  }
});

test('hadith come only from the chosen collections and are not duplicated', () => {
  const allowed = new Set(['Sahih al-Bukhari', 'Sahih Muslim', "An-Nawawi's Forty Hadith", 'Forty Hadith Qudsi']);
  const refs = new Set();
  for (const item of bundle.hadith) {
    assert.equal(item.type, 'hadith');
    assert.ok(allowed.has(item.source), item.source);
    assert.ok(!refs.has(item.ref), `duplicate ${item.ref}`);
    refs.add(item.ref);
    assert.doesNotMatch(item.en, /rest of the hadith|same as above|narration transmitted/i, item.ref);
  }
});
