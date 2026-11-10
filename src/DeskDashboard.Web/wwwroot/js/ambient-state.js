(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.AmbientState = factory();
  }
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  // Decides which display mode the dashboard shows, and where the ambient block sits.
  //
  //   focus   – full dashboard. Around each Jamaat (or Waqt when no Jamaat is set), while
  //             the user is interacting, or when ambient mode is off.
  //   ambient – dim clock + next prayer that moves every minute (burn-in protection).
  //   night   – tiny, dimmer clock from the night start time until Fajr begins.

  var MINUTE = 60 * 1000;
  var DAY_MINUTES = 24 * 60;

  var DEFAULTS = Object.freeze({
    enabled: true,
    focusBeforeMinutes: 15,
    focusAfterMinutes: 10,
    nightEnabled: true,
    nightStart: '23:00',
    ambientBrightness: 0.08,
    nightBrightness: 0.02,
    content: 'both'
  });

  var CONTENT_CHOICES = ['both', 'quran', 'hadith', 'off'];

  function clampNumber(value, min, max, fallback) {
    var number = Number(value);
    if (!Number.isFinite(number)) {
      return fallback;
    }
    return Math.min(max, Math.max(min, number));
  }

  function parseClock(value) {
    var match = /^(\d{1,2}):(\d{2})$/.exec(String(value || '').trim());
    if (!match) {
      return null;
    }
    var hours = Number(match[1]);
    var minutes = Number(match[2]);
    if (hours > 23 || minutes > 59) {
      return null;
    }
    return hours * 60 + minutes;
  }

  function normalizePrefs(raw, defaults) {
    var base = Object.assign({}, DEFAULTS, defaults || {});
    var input = raw && typeof raw === 'object' ? raw : {};
    return {
      enabled: typeof input.enabled === 'boolean' ? input.enabled : base.enabled,
      focusBeforeMinutes: Math.round(clampNumber(input.focusBeforeMinutes, 0, 60, base.focusBeforeMinutes)),
      focusAfterMinutes: Math.round(clampNumber(input.focusAfterMinutes, 0, 60, base.focusAfterMinutes)),
      nightEnabled: typeof input.nightEnabled === 'boolean' ? input.nightEnabled : base.nightEnabled,
      nightStart: parseClock(input.nightStart) !== null ? String(input.nightStart).trim() : base.nightStart,
      ambientBrightness: clampNumber(input.ambientBrightness, 0.01, 1, base.ambientBrightness),
      nightBrightness: clampNumber(input.nightBrightness, 0.01, 1, base.nightBrightness),
      content: CONTENT_CHOICES.indexOf(input.content) >= 0 ? input.content : base.content
    };
  }

  // Which kind of content this minute shows. With 'both' Quran and Hadith alternate by minute;
  // if one kind is empty the other is used.
  function typeForSlot(slot, choice, hasQuran, hasHadith) {
    if (choice === 'off') {
      return null;
    }
    var wanted = choice === 'quran' ? 'quran' : choice === 'hadith' ? 'hadith' : (slot % 2 === 0 ? 'quran' : 'hadith');
    if (wanted === 'quran' && !hasQuran) {
      wanted = hasHadith ? 'hadith' : null;
    } else if (wanted === 'hadith' && !hasHadith) {
      wanted = hasQuran ? 'quran' : null;
    }
    return wanted;
  }

  // The least recently shown item of a type (never shown first, then oldest), skipping removed
  // items and anything `skip(item)` rejects, such as text too long for this screen. New weekly
  // items have never been shown, so they come first. Ties go to the lowest id, so it is stable.
  function chooseNext(items, type, skip) {
    var best = null;
    var bestShown = 0;
    var list = Array.isArray(items) ? items : [];
    for (var i = 0; i < list.length; i += 1) {
      var item = list[i];
      if (!item || item.type !== type || item.hidden || (skip && skip(item))) {
        continue;
      }
      var shown = typeof item.lastShownAt === 'number' ? item.lastShownAt : -Infinity;
      if (best === null || shown < bestShown || (shown === bestShown && item.id < best.id)) {
        best = item;
        bestShown = shown;
      }
    }
    return best;
  }

  function toMs(value) {
    if (typeof value === 'number') {
      return Number.isFinite(value) ? value : null;
    }
    if (typeof value === 'string' && value) {
      var parsed = Date.parse(value);
      return Number.isFinite(parsed) ? parsed : null;
    }
    return null;
  }

  // One window per prayer: [anchor - before, anchor + after], anchor = Jamaat if set and valid,
  // otherwise the calculated Waqt start.
  function focusWindows(entries, prefs) {
    var windows = [];
    var seen = {};
    var list = Array.isArray(entries) ? entries : [];
    for (var i = 0; i < list.length; i += 1) {
      var entry = list[i];
      if (!entry || entry.isPrayer === false) {
        continue;
      }
      var jamaat = entry.jamaatTime && entry.jamaatWithinWindow !== false ? toMs(entry.jamaatAt) : null;
      var anchor = jamaat !== null ? jamaat : toMs(entry.at);
      if (anchor === null || seen[anchor]) {
        continue;
      }
      seen[anchor] = true;
      windows.push({
        key: entry.key,
        anchor: anchor,
        start: anchor - prefs.focusBeforeMinutes * MINUTE,
        end: anchor + prefs.focusAfterMinutes * MINUTE
      });
    }
    return windows;
  }

  // Night runs from nightStart (local clock) until the next Fajr Waqt.
  // minutesOfDay(ms) must return the local minute of day (0–1439) in the dashboard's time zone.
  function isNight(now, entries, prefs, minutesOfDay) {
    if (!prefs.nightEnabled || typeof minutesOfDay !== 'function') {
      return false;
    }
    var start = parseClock(prefs.nightStart);
    if (start === null) {
      return false;
    }
    var list = Array.isArray(entries) ? entries : [];
    for (var i = 0; i < list.length; i += 1) {
      var entry = list[i];
      if (!entry || entry.key !== 'fajr') {
        continue;
      }
      var fajr = toMs(entry.at);
      if (fajr === null || now >= fajr) {
        continue;
      }
      var fajrMinute = minutesOfDay(fajr);
      var span = ((fajrMinute - start) % DAY_MINUTES + DAY_MINUTES) % DAY_MINUTES;
      if (span === 0) {
        continue;
      }
      if (now >= fajr - span * MINUTE) {
        return true;
      }
    }
    return false;
  }

  // The focus window containing `now`, or null.
  function activeWindow(now, entries, prefs) {
    var windows = focusWindows(entries, prefs);
    for (var i = 0; i < windows.length; i += 1) {
      if (now >= windows[i].start && now < windows[i].end) {
        return windows[i];
      }
    }
    return null;
  }

  // input.aod (the AOD button): { skipUntil }. While now < skipUntil, the focus windows that were
  // already running when it was pressed are skipped. A focus reason after that ends AOD, and the
  // result says so (aodEnded) so the caller can clear it.
  function decideMode(input) {
    var prefs = input.prefs;
    var now = input.now;
    var aod = input.aod || null;

    if (input.forceFocus || (input.wakeUntil && now < input.wakeUntil)) {
      return { mode: 'focus', reason: input.forceFocus ? 'forced' : 'wake' };
    }
    if (!prefs.enabled && !aod) {
      return { mode: 'focus', reason: 'disabled' };
    }

    var phase = input.phase;
    var jamaatFocus = phase === 'approaching' || phase === 'reached';
    var window = activeWindow(now, input.entries, prefs);
    if (jamaatFocus || window) {
      if (!(aod && now < aod.skipUntil)) {
        return {
          mode: 'focus',
          reason: jamaatFocus ? 'jamaat' : 'prayer',
          key: window ? window.key : null,
          aodEnded: Boolean(aod)
        };
      }
    }

    if (isNight(now, input.entries, prefs, input.minutesOfDay)) {
      return { mode: 'night', reason: aod ? 'aod-night' : 'night' };
    }

    return { mode: 'ambient', reason: aod ? 'aod' : 'idle' };
  }

  // Where the ambient block sits within the free space, as fractions 0–1. Changes every minute
  // and walks a low-discrepancy sequence so successive positions are spread over the screen
  // instead of clustering, which spreads wear evenly across the pixels.
  function blockPosition(now) {
    var slot = Math.floor(now / MINUTE);
    var fx = (slot * 0.6180339887) % 1;
    var fy = (slot * 0.7548776662 + 0.5) % 1;
    return { slot: slot, fx: fx, fy: fy };
  }

  function brightnessFor(mode, prefs) {
    if (mode === 'night') {
      return prefs.nightBrightness;
    }
    if (mode === 'ambient') {
      return prefs.ambientBrightness;
    }
    return -1;
  }

  return {
    DEFAULTS: DEFAULTS,
    normalizePrefs: normalizePrefs,
    parseClock: parseClock,
    focusWindows: focusWindows,
    isNight: isNight,
    decideMode: decideMode,
    blockPosition: blockPosition,
    brightnessFor: brightnessFor,
    typeForSlot: typeForSlot,
    chooseNext: chooseNext,
    activeWindow: activeWindow
  };
});
