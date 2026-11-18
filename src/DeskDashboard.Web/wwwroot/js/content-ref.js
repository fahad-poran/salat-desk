// The short reference copied from the Library, e.g. "Quran (9:10)" or "Hadis (Bukhari: 2/3)".
// Pure and DOM-free (tests: tests/js/content-ref.test.js).
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = api;
  } else {
    root.ContentRef = api;
  }
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  // Collections with a single book use the hadith number alone: "Hadis (Nawawi 40: 12)".
  const BOOKS = {
    bukhari: { name: 'Bukhari', singleBook: false },
    muslim: { name: 'Muslim', singleBook: false },
    nawawi: { name: 'Nawawi 40', singleBook: true },
    qudsi: { name: 'Qudsi 40', singleBook: true }
  };

  /**
   * item: { type, key ("quran:9:10", "quran:94:5-8", "bukhari:6168"), refBook, refNumber }.
   * Hadith use the in-book reference (book/hadith) when known, otherwise the collection number.
   */
  function format(item) {
    if (!item || typeof item.key !== 'string') {
      return '';
    }
    const parts = item.key.split(':');
    if (parts[0] === 'quran' && parts.length >= 3) {
      return 'Quran (' + parts[1] + ':' + parts[2] + ')';
    }

    const book = BOOKS[parts[0]] || { name: parts[0].charAt(0).toUpperCase() + parts[0].slice(1), singleBook: false };
    const hasRef = Number.isInteger(item.refBook) && Number.isInteger(item.refNumber);
    let number = parts[1] || '';
    if (hasRef) {
      number = book.singleBook ? String(item.refNumber) : item.refBook + '/' + item.refNumber;
    }
    return 'Hadis (' + book.name + ': ' + number + ')';
  }

  return { format: format };
});
