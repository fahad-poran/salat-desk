(function (root, factory) {
  if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.JamaatState = factory();
  }
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';

  var MINUTE = 60 * 1000;
  var WARNING_MS = 10 * MINUTE;
  var REACHED_GRACE_MS = MINUTE;
  var PASSED_GRACE_MS = 5 * MINUTE;

  function toMs(value) {
    if (value instanceof Date) {
      return Number.isFinite(value.getTime()) ? value.getTime() : null;
    }
    if (typeof value === 'number') {
      return Number.isFinite(value) ? value : null;
    }
    if (typeof value === 'string' && value) {
      var parsed = Date.parse(value);
      return Number.isFinite(parsed) ? parsed : null;
    }
    return null;
  }

  function toCandidate(entry) {
    if (!entry || entry.isPrayer === false) {
      return null;
    }

    var at = toMs(entry.at);
    if (at === null) {
      return null;
    }

    var withinWindow = entry.jamaatWithinWindow !== false;
    var jamaatAt = entry.jamaatTime && withinWindow ? toMs(entry.jamaatAt) : null;

    return {
      key: String(entry.key || ''),
      name: entry.name || entry.key || '',
      at: at,
      jamaatAt: jamaatAt,
      jamaatTime: entry.jamaatTime || null,
      jamaatWithinWindow: withinWindow,
      validationError: entry.jamaatValidationError || null,
      nextStartMs: null
    };
  }

  // Warning length for one prayer: the user's minutes for that prayer (default 10), never longer
  // than the time between the Waqt start and the Jamaat.
  function warningMsFor(prayer, options) {
    var minutes = options && options.warningMinutes && Number(options.warningMinutes[prayer.key]);
    var wanted = Number.isFinite(minutes) && minutes > 0 ? minutes * MINUTE : WARNING_MS;
    if (prayer.jamaatAt && prayer.at && prayer.jamaatAt > prayer.at) {
      return Math.min(wanted, prayer.jamaatAt - prayer.at);
    }
    return wanted;
  }

  function phaseFor(prayer, now, warningMs) {
    if (now < prayer.at) {
      return { phase: 'upcoming', targetAt: prayer.at, targetKind: 'waqt' };
    }

    if (!prayer.jamaatWithinWindow) {
      return { phase: 'invalid', targetAt: prayer.nextStartMs, targetKind: 'next' };
    }

    if (!prayer.jamaatAt) {
      return { phase: 'missing', targetAt: prayer.nextStartMs, targetKind: 'next' };
    }

    if (now < prayer.jamaatAt) {
      return {
        phase: prayer.jamaatAt - now <= warningMs ? 'approaching' : 'waiting',
        targetAt: prayer.jamaatAt,
        targetKind: 'jamaat'
      };
    }

    if (now < prayer.jamaatAt + REACHED_GRACE_MS) {
      return { phase: 'reached', targetAt: prayer.jamaatAt, targetKind: 'jamaat' };
    }

    if (now < prayer.jamaatAt + PASSED_GRACE_MS) {
      return { phase: 'passed', targetAt: prayer.jamaatAt, targetKind: 'jamaat' };
    }

    return null;
  }

  function evaluate(entries, nowMs, options) {
    var parsedNow = toMs(nowMs);
    var now = parsedNow === null ? Date.now() : parsedNow;
    var seen = new Set();
    var candidates = [];

    for (var i = 0; i < (Array.isArray(entries) ? entries.length : 0); i += 1) {
      var candidate = toCandidate(entries[i]);
      if (!candidate) {
        continue;
      }

      var identity = candidate.key + '|' + candidate.at;
      if (seen.has(identity)) {
        continue;
      }

      seen.add(identity);
      candidates.push(candidate);
    }

    candidates.sort(function (a, b) {
      return a.at - b.at;
    });

    for (var index = 0; index < candidates.length; index += 1) {
      candidates[index].nextStartMs = index + 1 < candidates.length ? candidates[index + 1].at : null;
    }

    for (var position = 0; position < candidates.length; position += 1) {
      var prayer = candidates[position];
      var warningMs = warningMsFor(prayer, options);
      var info = phaseFor(prayer, now, warningMs);
      if (!info) {
        continue;
      }

      if (
        (info.phase === 'missing' || info.phase === 'invalid') &&
        prayer.nextStartMs !== null &&
        now >= prayer.nextStartMs
      ) {
        continue;
      }

      var remainingMs = info.targetAt === null || info.targetAt === undefined
        ? null
        : Math.max(0, info.targetAt - now);

      return {
        phase: info.phase,
        prayer: prayer,
        targetAt: info.targetAt === undefined ? null : info.targetAt,
        targetKind: info.targetKind,
        remainingMs: remainingMs,
        warning: info.phase === 'approaching',
        warningMs: warningMs,
        eventKey: prayer.jamaatAt ? prayer.key + '|' + prayer.jamaatAt : null
      };
    }

    return {
      phase: 'idle',
      prayer: null,
      targetAt: null,
      targetKind: null,
      remainingMs: null,
      warning: false,
      eventKey: null
    };
  }

  function splitDuration(ms) {
    var totalSeconds = Math.max(0, Math.floor((Number.isFinite(ms) ? ms : 0) / 1000));
    var hours = Math.floor(totalSeconds / 3600);
    var minutes = Math.floor((totalSeconds % 3600) / 60);
    var seconds = totalSeconds % 60;

    return {
      hours: hours,
      minutes: minutes,
      seconds: seconds,
      totalMinutes: Math.ceil(totalSeconds / 60),
      clock: [
        String(hours).padStart(2, '0'),
        String(minutes).padStart(2, '0'),
        String(seconds).padStart(2, '0')
      ].join(':')
    };
  }

  var PRAYER_KEYS = ['fajr', 'dhuhr', 'asr', 'maghrib', 'isha'];

  // Per-prayer alert settings: { fajr: { notify: true, minutes: 10 }, ... }.
  function normalizeAlertPrefs(raw) {
    var input = raw && typeof raw === 'object' ? raw : {};
    var result = {};
    for (var i = 0; i < PRAYER_KEYS.length; i += 1) {
      var key = PRAYER_KEYS[i];
      var item = input[key] && typeof input[key] === 'object' ? input[key] : {};
      var minutes = Math.round(Number(item.minutes));
      result[key] = {
        notify: typeof item.notify === 'boolean' ? item.notify : true,
        minutes: Number.isFinite(minutes) ? Math.min(120, Math.max(1, minutes)) : 10
      };
    }
    return result;
  }

  function warningMinutesOf(alertPrefs) {
    var result = {};
    for (var key in alertPrefs) {
      if (Object.prototype.hasOwnProperty.call(alertPrefs, key)) {
        result[key] = alertPrefs[key].minutes;
      }
    }
    return result;
  }

  // Upcoming phone notifications: one per prayer with Notify on and a valid Jamaat whose warning
  // time is still ahead. id is stable for the same prayer and Jamaat time.
  function alertSchedule(entries, alertPrefs, nowMs) {
    var now = toMs(nowMs);
    var schedule = [];
    var seen = {};
    var list = Array.isArray(entries) ? entries : [];
    for (var i = 0; i < list.length; i += 1) {
      var prayer = toCandidate(list[i]);
      if (!prayer || !prayer.jamaatAt || !alertPrefs[prayer.key] || !alertPrefs[prayer.key].notify) {
        continue;
      }
      var warnMs = warningMsFor(prayer, { warningMinutes: warningMinutesOf(alertPrefs) });
      var warnAt = prayer.jamaatAt - warnMs;
      var id = prayer.key + '|' + prayer.jamaatAt;
      if (warnAt <= now || seen[id]) {
        continue;
      }
      seen[id] = true;
      schedule.push({
        id: id,
        key: prayer.key,
        name: prayer.name,
        jamaatAt: prayer.jamaatAt,
        warnAt: warnAt,
        minutes: Math.round(warnMs / MINUTE)
      });
    }
    schedule.sort(function (a, b) { return a.warnAt - b.warnAt; });
    return schedule;
  }

  return {
    WARNING_MS: WARNING_MS,
    PRAYER_KEYS: PRAYER_KEYS,
    warningMsFor: function (entry, options) { return warningMsFor(toCandidate(entry) || entry, options); },
    normalizeAlertPrefs: normalizeAlertPrefs,
    warningMinutesOf: warningMinutesOf,
    alertSchedule: alertSchedule,
    REACHED_GRACE_MS: REACHED_GRACE_MS,
    PASSED_GRACE_MS: PASSED_GRACE_MS,
    evaluate: evaluate,
    splitDuration: splitDuration
  };
});
