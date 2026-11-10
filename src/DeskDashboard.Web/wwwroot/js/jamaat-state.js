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

  function phaseFor(prayer, now) {
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
        phase: prayer.jamaatAt - now <= WARNING_MS ? 'approaching' : 'waiting',
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

  function evaluate(entries, nowMs) {
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
      var info = phaseFor(prayer, now);
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

  return {
    WARNING_MS: WARNING_MS,
    REACHED_GRACE_MS: REACHED_GRACE_MS,
    PASSED_GRACE_MS: PASSED_GRACE_MS,
    evaluate: evaluate,
    splitDuration: splitDuration
  };
});
