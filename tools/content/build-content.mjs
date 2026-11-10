// Builds the starter Ayah/Hadith set that ships inside the app (first run, offline).
//
//   node tools/content/build-content.mjs [cacheDir]
//
// Downloads the source data once (or reuses cacheDir), selects short, self-contained items and
// writes src/DeskDashboard.Core/Content/seed-content.json (embedded in DeskDashboard.Core).
// Quran: the "seed" block of src/DeskDashboard.Core/Content/quran-pool.txt. After first run the
// app adds more each week itself (ContentRefreshService), using the same text rules
// (ContentText.cs). Keep the rules here and there in step.
//
// Sources
//   Quran Arabic   : Tanzil Uthmani text via AlQuran.cloud (edition quran-uthmani)
//   Quran English  : Sahih International via AlQuran.cloud (edition en.sahih)
//   Hadith         : fawazahmed0/hadith-api (Unlicense) via jsDelivr

import { mkdir, readFile, writeFile, access } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '..', '..');
const cacheDir = path.resolve(process.argv[2] || path.join(root, 'tools', 'content', '.cache'));
const contentDir = path.join(root, 'src', 'DeskDashboard.Core', 'Content');
const outFile = path.join(contentDir, 'seed-content.json');
const poolFile = path.join(contentDir, 'quran-pool.txt');

const HADITH_BASE = 'https://cdn.jsdelivr.net/gh/fawazahmed0/hadith-api@1/editions';
const QURAN_BASE = 'https://api.alquran.cloud/v1/quran';

// Length limits keep each item readable on a phone in ambient mode.
const LIMITS = {
  quran: { ar: 360, en: 400 }, // = ContentText.cs
  hadith: { ar: 420, en: 360 },
  minEn: 40
};

// Collections and the books (sections) taken from them. Reminder-style books only: no
// legal rulings, punishments or narrations that need surrounding context.
const HADITH_SOURCES = [
  { key: 'nawawi', name: "An-Nawawi's Forty Hadith", grade: null, sections: null, max: 60 },
  { key: 'qudsi', name: 'Forty Hadith Qudsi', grade: null, sections: null, max: 60 },
  { key: 'bukhari', name: 'Sahih al-Bukhari', grade: 'Sahih', sections: [2, 3, 78, 80, 81], max: 140 },
  { key: 'muslim', name: 'Sahih Muslim', grade: 'Sahih', sections: [45, 48, 49, 50, 55], max: 140 }
];

// Narrations that point at other narrations or only list chains do not stand on their own.
const DEPENDENT = /\b(same as|similar (to|hadith|narration)|previous|aforesaid|above[- ]mentioned|mentioned above|like this|this hadith (has been|was)? ?(narrated|transmitted)|(narrated|transmitted) (this|the|a) (hadith|tradition)|through (another|a different|the same) chain|chain of (narrators|transmitters)|rest of the (hadith|tradition)|in the (narration|version|hadith) (transmitted|narrated)|hadith is the same|but i did not hear|with (this|the following|an?) (addition|change|variation)|with (a|the) (slight|minor) (change|variation|difference)|(words|wording) (are|is) |ditto)\b/i;

async function exists(file) {
  try {
    await access(file);
    return true;
  } catch {
    return false;
  }
}

async function cached(name, url) {
  const file = path.join(cacheDir, name);
  if (!(await exists(file))) {
    console.log(`Downloading ${url}`);
    const response = await fetch(url, { headers: { 'User-Agent': 'DeskDashboard-content-build' } });
    if (!response.ok) {
      throw new Error(`${url} returned ${response.status}`);
    }
    await writeFile(file, await response.text());
  }
  return JSON.parse(await readFile(file, 'utf8'));
}

// Source texts occasionally contain HTML line breaks or tags; the app shows plain text.
const clean = (text) => String(text || '')
  .replace(/<br\s*\/?>/gi, ' ')
  .replace(/<\/?[a-z][^>]*>/gi, '')
  .replace(/\s+/g, ' ')
  .trim();

function parseSelection(text) {
  const refs = [];
  let seed = true;
  for (const raw of text.split(/\r?\n/)) {
    if (raw.includes('===== weekly =====')) {
      seed = false;
      continue;
    }
    const line = raw.replace(/#.*/, '').trim();
    if (!line) {
      continue;
    }
    const match = /^(\d{1,3}):(\d{1,3})(?:-(\d{1,3}))?$/.exec(line);
    if (!match) {
      throw new Error(`Bad Quran reference: "${line}"`);
    }
    const surah = Number(match[1]);
    const first = Number(match[2]);
    const last = Number(match[3] || match[2]);
    if (last < first) {
      throw new Error(`Bad range: "${line}"`);
    }
    refs.push({ surah, first, last, label: line, seed });
  }
  return refs;
}

async function buildQuran(report) {
  const arabic = (await cached('quran-uthmani.json', `${QURAN_BASE}/quran-uthmani`)).data;
  const english = (await cached('en-sahih.json', `${QURAN_BASE}/en.sahih`)).data;
  // Every reference in the pool is checked here (range, length); only the seed block is bundled.
  const refs = parseSelection(await readFile(poolFile, 'utf8'));

  // AlQuran.cloud prefixes the Basmala to ayah 1 of every surah except Al-Fatiha (where it is
  // ayah 1 itself) and At-Tawbah. Remove it so the text matches the ayah.
  const basmala = arabic.surahs[0].ayahs[0].text.trim();

  const items = [];
  for (const ref of refs) {
    const surahAr = arabic.surahs[ref.surah - 1];
    const surahEn = english.surahs[ref.surah - 1];
    if (!surahAr || ref.last > surahAr.ayahs.length) {
      throw new Error(`Reference out of range: ${ref.label}`);
    }

    const ar = [];
    const en = [];
    for (let ayah = ref.first; ayah <= ref.last; ayah += 1) {
      let text = surahAr.ayahs[ayah - 1].text.replace(/^﻿/, '').trim();
      if (ayah === 1 && ref.surah !== 1 && text.startsWith(basmala)) {
        text = text.slice(basmala.length).trim();
      }
      // An end-of-ayah mark with its number between joined ayat.
      ar.push(ref.first === ref.last ? text : `${text} ﴿${toArabicDigits(ayah)}﴾`);
      en.push(clean(surahEn.ayahs[ayah - 1].text));
    }

    const item = {
      type: 'quran',
      key: ref.first === ref.last ? `quran:${ref.surah}:${ref.first}` : `quran:${ref.surah}:${ref.first}-${ref.last}`,
      ar: clean(ar.join(' ')),
      en: en.join(' '),
      ref: `${surahEn.englishName} ${ref.surah}:${ref.first}${ref.last !== ref.first ? `–${ref.last}` : ''}`,
      source: 'Quran'
    };

    if (item.ar.length > LIMITS.quran.ar || item.en.length > LIMITS.quran.en) {
      report.droppedQuran.push(`${ref.label}${ref.seed ? '' : ' (weekly)'} (ar ${item.ar.length}, en ${item.en.length})`);
      continue;
    }
    if (ref.seed) {
      items.push(item);
    } else {
      report.weeklyQuran += 1;
    }
  }
  return items;
}

function toArabicDigits(number) {
  return String(number).replace(/\d/g, (digit) => '٠١٢٣٤٥٦٧٨٩'[Number(digit)]);
}

function sectionOf(metadata, hadithNumber) {
  const details = metadata.section_details || metadata.section_detail || {};
  for (const [section, range] of Object.entries(details)) {
    if (hadithNumber >= range.hadithnumber_first && hadithNumber <= range.hadithnumber_last) {
      return Number(section);
    }
  }
  return null;
}

// The standard (Arabic edition) number used by sunnah.com and most books, e.g. Muslim
// 2609.01 → "2609a". Falls back to the dataset's own number.
function referenceNumber(hadith) {
  const value = Number(hadith.arabicnumber);
  if (!Number.isFinite(value) || value <= 0) {
    return String(hadith.hadithnumber);
  }
  const whole = Math.floor(value);
  const part = Math.round((value - whole) * 100);
  return part > 0 && part <= 26 ? `${whole}${String.fromCharCode(96 + part)}` : String(whole);
}

function spread(list, max) {
  if (list.length <= max) {
    return list;
  }
  const step = list.length / max;
  return Array.from({ length: max }, (_, index) => list[Math.floor(index * step)]);
}

async function buildHadith(report) {
  const items = [];
  for (const source of HADITH_SOURCES) {
    const eng = await cached(`eng-${source.key}.json`, `${HADITH_BASE}/eng-${source.key}.min.json`);
    const ara = await cached(`ara-${source.key}.json`, `${HADITH_BASE}/ara-${source.key}.min.json`);
    const arabicByNumber = new Map(ara.hadiths.map((hadith) => [hadith.hadithnumber, hadith]));

    const candidates = [];
    const seen = new Set();
    let considered = 0;
    for (const hadith of eng.hadiths) {
      const section = sectionOf(eng.metadata, hadith.hadithnumber);
      if (source.sections && !source.sections.includes(section)) {
        continue;
      }
      considered += 1;

      const en = clean(hadith.text)
        .replace(/([a-z])- ([a-z])/g, '$1$2') // print line-break hyphens: "Mes- senger"
        .replace(/([a-z)\]]):(?=[A-Z“"'(])/g, '$1: '); // "Narrated Anas:The" → "Narrated Anas: The"
      const ar = clean(arabicByNumber.get(hadith.hadithnumber)?.text);
      if (!en || !ar || en.length < LIMITS.minEn) {
        continue;
      }
      if (en.length > LIMITS.hadith.en || ar.length > LIMITS.hadith.ar || DEPENDENT.test(en)) {
        continue;
      }
      // Skip anything graded weak or fabricated, if the collection carries grades.
      const grades = (hadith.grades || []).map((grade) => String(grade.grade || '').toLowerCase());
      if (grades.some((grade) => /da'?if|daif|weak|mawdu|fabricated|munkar/.test(grade))) {
        continue;
      }
      if (seen.has(en)) {
        continue;
      }
      seen.add(en);

      candidates.push({
        type: 'hadith',
        key: `${source.key}:${referenceNumber(hadith)}`,
        ar,
        en,
        ref: `${source.name} ${referenceNumber(hadith)}`,
        source: source.name,
        ...(source.grade ? { grade: source.grade } : {})
      });
    }

    const chosen = spread(candidates, source.max);
    report.hadith.push(`${source.name}: ${considered} in selected books, ${candidates.length} fit, ${chosen.length} used`);
    items.push(...chosen);
  }
  return items;
}

async function readExclusions() {
  const file = path.join(here, 'exclude.txt');
  if (!(await exists(file))) {
    return new Set();
  }
  const refs = (await readFile(file, 'utf8'))
    .split(/\r?\n/)
    .map((line) => line.replace(/#.*/, '').trim())
    .filter(Boolean);
  return new Set(refs);
}

async function main() {
  await mkdir(cacheDir, { recursive: true });
  const report = { droppedQuran: [], hadith: [], weeklyQuran: 0 };

  // References removed after review (docs/content-review.html → tools/content/exclude.txt).
  const excluded = await readExclusions();
  const keep = (item) => !excluded.has(item.ref);
  const quranAll = await buildQuran(report);
  const hadithAll = await buildHadith(report);
  const quran = quranAll.filter(keep);
  const hadith = hadithAll.filter(keep);
  const known = new Set([...quranAll, ...hadithAll].map((item) => item.ref));
  const unknown = [...excluded].filter((ref) => !known.has(ref));

  const bundle = {
    version: 1,
    generated: new Date().toISOString().slice(0, 10),
    credits: [
      'Quran Arabic text: Tanzil.net (Uthmani), via AlQuran.cloud.',
      'Quran English translation: Sahih International, via AlQuran.cloud.',
      'Hadith Arabic and English: fawazahmed0/hadith-api (public domain).'
    ],
    quran,
    hadith
  };

  await mkdir(path.dirname(outFile), { recursive: true });
  await writeFile(outFile, `${JSON.stringify(bundle, null, 1)}\n`);

  console.log(`Quran: ${quran.length} seed items, ${report.weeklyQuran} weekly references fit the screen` +
    (report.droppedQuran.length ? `; too long (never shown): ${report.droppedQuran.join(', ')}` : ''));
  for (const line of report.hadith) {
    console.log(line);
  }
  console.log(`Excluded after review: ${excluded.size - unknown.length}` +
    (unknown.length ? `; not found in the bundle (check spelling): ${unknown.join(', ')}` : ''));
  console.log(`Wrote ${path.relative(root, outFile)}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
