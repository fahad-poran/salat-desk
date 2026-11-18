// How each prayer's Jamaat time is set (fixed time, Waqt + minutes, or the next rounded slot), for the
// Settings preview. Mirrors JamaatRule / JamaatResolver in DeskDashboard.Core, which is what actually
// decides the times; this copy only shows the user what their choice gives. Pure and DOM-free (tests:
// tests/js/jamaat-rules.test.js). Times are minutes of the day; a window that passes midnight continues
// past 1440.
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.JamaatRules = api;
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  const MODES = ['fixed', 'offset', 'slot'];
  const FALLBACK_MODES = ['offset', 'slot', 'warn'];
  const SLOT_SIZES = [5, 10, 15, 20, 30, 60];
  const DEFAULT_FALLBACK = { mode: 'offset', minutes: 10 };
  const DEFAULT_RULE = { mode: 'fixed', fallback: DEFAULT_FALLBACK };

  function int(value, fallback, min, max) {
    const number = Math.round(Number(value));
    if (!Number.isFinite(number)) {
      return fallback;
    }
    return Math.min(max, Math.max(min, number));
  }

  function normalizePart(raw, modes, fallbackMode) {
    const source = raw && typeof raw === 'object' ? raw : {};
    const mode = modes.indexOf(source.mode) >= 0 ? source.mode : fallbackMode;
    if (mode === 'offset') {
      return { mode: 'offset', minutes: int(source.minutes, 10, 0, 120) };
    }
    if (mode === 'slot') {
      const slot = SLOT_SIZES.indexOf(Number(source.slot)) >= 0 ? Number(source.slot) : 15;
      return { mode: 'slot', slot: slot, gap: int(source.gap, 5, 0, 60) };
    }
    return { mode: mode };
  }

  /** A complete, valid rule. Missing or bad values get the defaults (fixed; Waqt + 10 when passed). */
  function normalizeRule(raw) {
    const rule = normalizePart(raw, MODES, 'fixed');
    if (rule.mode === 'fixed') {
      rule.fallback = normalizePart(raw && raw.fallback ? raw.fallback : DEFAULT_FALLBACK, FALLBACK_MODES, 'offset');
    }
    return rule;
  }

  function parseTime(value) {
    const match = /^(\d{1,2}):(\d{2})/.exec(value || '');
    if (!match) {
      return null;
    }
    const hour = Number(match[1]);
    const minute = Number(match[2]);
    return hour > 23 || minute > 59 ? null : hour * 60 + minute;
  }

  function formatTime(minutes) {
    const value = ((minutes % 1440) + 1440) % 1440;
    const hour24 = Math.floor(value / 60);
    const minute = value % 60;
    const hour12 = hour24 % 12 || 12;
    return hour12 + ':' + String(minute).padStart(2, '0') + ' ' + (hour24 >= 12 ? 'PM' : 'AM');
  }

  /**
   * A prayer window from the API's jamaatEarliest / jamaatLatest ("HH:mm"): { start, latest } in minutes,
   * latest continuing past 1440 when the window passes midnight (Isha). Null when unavailable.
   */
  function windowFrom(earliest, latest) {
    const start = parseTime(earliest);
    let last = parseTime(latest);
    if (start === null || last === null) {
      return null;
    }
    if (last < start) {
      last += 1440;
    }
    return { start: start, latest: last };
  }

  function dynamic(part, window) {
    let at;
    if (part.mode === 'slot') {
      const earliest = window.start + part.gap;
      at = Math.ceil(earliest / part.slot) * part.slot;
    } else {
      at = window.start + part.minutes;
    }
    const capped = at > window.latest;
    return { at: capped ? window.latest : at, capped: capped };
  }

  /**
   * The Jamaat time a rule gives in a window: { at (minutes), valid, adjusted, capped } or null when no
   * Jamaat is set (fixed mode without a time).
   */
  function resolve(rawRule, fixedTime, window) {
    const rule = normalizeRule(rawRule);
    if (!window) {
      return null;
    }
    if (rule.mode === 'offset' || rule.mode === 'slot') {
      const result = dynamic(rule, window);
      return { at: result.at, valid: true, adjusted: false, capped: result.capped };
    }

    let time = parseTime(fixedTime);
    if (time === null) {
      return null;
    }
    if (window.latest >= 1440 && time < window.start) {
      time += 1440; // after midnight, inside an Isha window
    }
    if (time >= window.start && time <= window.latest) {
      return { at: time, valid: true, adjusted: false, capped: false };
    }
    if (rule.fallback.mode === 'warn') {
      return { at: time, valid: false, adjusted: false, capped: false };
    }
    const result = dynamic(rule.fallback, window);
    return { at: result.at, valid: true, adjusted: true, capped: result.capped };
  }

  /** Short text for a rule part, e.g. "Waqt + 5 min" or "next 15-min slot, 5+ min after Waqt". */
  function describe(part) {
    if (part.mode === 'offset') {
      return 'Waqt + ' + part.minutes + ' min';
    }
    if (part.mode === 'slot') {
      return 'next ' + part.slot + '-min slot, at least ' + part.gap + ' min after Waqt';
    }
    if (part.mode === 'warn') {
      return 'keep the time and warn';
    }
    return 'fixed time';
  }

  return {
    MODES: MODES,
    FALLBACK_MODES: FALLBACK_MODES,
    SLOT_SIZES: SLOT_SIZES,
    DEFAULT_RULE: DEFAULT_RULE,
    normalizeRule: normalizeRule,
    parseTime: parseTime,
    formatTime: formatTime,
    windowFrom: windowFrom,
    resolve: resolve,
    describe: describe
  };
});
