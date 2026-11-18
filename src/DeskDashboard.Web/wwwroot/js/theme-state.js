// Appearance: accent colour and clock font. Pure and DOM-free (tests: tests/js/theme-state.test.js);
// app.js applies the result as CSS custom properties on :root.
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.ThemeState = api;
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // Red and orange are left out on purpose: they are the Jamaat warning colours.
  const ACCENTS = [
    { key: 'teal', label: 'Teal', hex: '#5eead4' },
    { key: 'emerald', label: 'Emerald', hex: '#6ee7a0' },
    { key: 'sky', label: 'Sky', hex: '#7dd3fc' },
    { key: 'gold', label: 'Gold', hex: '#f5c86b' },
    { key: 'rose', label: 'Rose', hex: '#f9a8d4' },
    { key: 'violet', label: 'Violet', hex: '#c4b5fd' },
    { key: 'silver', label: 'Silver', hex: '#d4d4d8' }
  ];

  // Clock fonts. The web fonts are bundled in wwwroot/fonts (SIL Open Font License, see NOTICE).
  const FONTS = [
    { key: 'system', label: 'System', family: 'var(--font)', weight: 300 },
    { key: 'thin', label: 'Thin', family: 'var(--font)', weight: 200 },
    { key: 'rounded', label: 'Rounded', family: '"Nunito", var(--font)', weight: 300 },
    { key: 'mono', label: 'Mono', family: '"JetBrains Mono", ui-monospace, monospace', weight: 300 },
    { key: 'serif', label: 'Serif', family: '"DM Serif Display", Georgia, serif', weight: 400 },
    { key: 'digital', label: 'Digital', family: '"Orbitron", var(--font)', weight: 400 }
  ];

  const DEFAULTS = { accent: '#5eead4', font: 'system' };

  function normalizeHex(value) {
    const text = String(value || '').trim().toLowerCase();
    if (/^#[0-9a-f]{6}$/.test(text)) {
      return text;
    }
    if (/^#[0-9a-f]{3}$/.test(text)) {
      return '#' + text[1] + text[1] + text[2] + text[2] + text[3] + text[3];
    }
    return null;
  }

  function normalize(raw) {
    const source = raw && typeof raw === 'object' ? raw : {};
    const font = FONTS.some(function (item) { return item.key === source.font; }) ? source.font : DEFAULTS.font;
    return { accent: normalizeHex(source.accent) || DEFAULTS.accent, font: font };
  }

  function rgb(hex) {
    const value = parseInt(hex.slice(1), 16);
    return [(value >> 16) & 255, (value >> 8) & 255, value & 255];
  }

  function toHex(parts) {
    return '#' + parts.map(function (part) {
      return Math.round(Math.min(255, Math.max(0, part))).toString(16).padStart(2, '0');
    }).join('');
  }

  // Mix a colour with another: amount 0 = colour, 1 = other.
  function mix(parts, other, amount) {
    return parts.map(function (part, index) { return part + (other[index] - part) * amount; });
  }

  /** Every shade the stylesheet needs, from one accent colour. */
  function palette(hex) {
    const base = rgb(normalizeHex(hex) || DEFAULTS.accent);
    const black = [0, 0, 0];
    const white = [255, 255, 255];
    return {
      '--accent': toHex(base),
      '--accent-rgb': base.map(Math.round).join(', '),
      '--accent-hover': toHex(mix(base, white, 0.35)),
      '--accent-ink': toHex(mix(base, black, 0.85)),
      '--accent-bg': toHex(mix(base, black, 0.89)),
      '--ambient-accent': toHex(mix(base, black, 0.18))
    };
  }

  function fontFor(key) {
    return FONTS.find(function (item) { return item.key === key; }) || FONTS[0];
  }

  return {
    ACCENTS: ACCENTS,
    FONTS: FONTS,
    DEFAULTS: DEFAULTS,
    normalize: normalize,
    normalizeHex: normalizeHex,
    palette: palette,
    fontFor: fontFor
  };
});
