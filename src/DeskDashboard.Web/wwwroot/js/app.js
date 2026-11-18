(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);

  // The standalone app hosts this page in a .NET MAUI HybridWebView (origin https://0.0.0.1 on
  // Android, app://0.0.0.1 on iOS). There is no server, so API calls go to C# through the bridge.
  const isNativeHost = window.location.hostname === '0.0.0.1';
  let nativeBridge = null;

  const elements = {
    locationLabel: $('locationLabel'),
    connection: $('connection'),
    statusText: $('statusText'),
    settingsButton: $('settingsButton'),
    clockInner: $('clockInner'),
    clockMain: $('clockMain'),
    clockSeconds: $('clockSeconds'),
    clockMeridiem: $('clockMeridiem'),
    clockDate: $('clockDate'),
    clockZone: $('clockZone'),
    nextPanel: $('nextPanel'),
    nextLabel: $('nextLabel'),
    nextName: $('nextName'),
    nextBadge: $('nextBadge'),
    nextTime: $('nextTime'),
    nextMeridiem: $('nextMeridiem'),
    nextCaption: $('nextCaption'),
    warningChip: $('warningChip'),
    warningNote: $('warningNote'),
    countdownLabel: $('countdownLabel'),
    countdown: $('countdown'),
    prayerDate: $('prayerDate'),
    methodChip: $('methodChip'),
    prayerList: $('prayerList'),
    disclaimer: $('disclaimer'),
    lastUpdated: $('lastUpdated'),
    banner: $('banner'),
    toast: $('toast'),
    settingsDialog: $('settingsDialog'),
    settingsForm: $('settingsForm'),
    closeSettingsButton: $('closeSettingsButton'),
    cancelSettingsButton: $('cancelSettingsButton'),
    saveSettingsButton: $('saveSettingsButton'),
    settingsErrors: $('settingsErrors'),
    settingsTabs: $('settingsTabs'),
    settingsDirty: $('settingsDirty'),
    jamaatCards: $('jamaatCards'),
    fontChoices: $('fontChoices'),
    accentChoices: $('accentChoices'),
    accentCustomInput: $('accentCustomInput'),
    themePreview: $('themePreview'),
    searchInput: $('searchInput'),
    searchButton: $('searchButton'),
    searchResults: $('searchResults'),
    searchStatus: $('searchStatus'),
    countryInput: $('countryInput'),
    cityInput: $('cityInput'),
    latitudeInput: $('latitudeInput'),
    longitudeInput: $('longitudeInput'),
    timeZoneInput: $('timeZoneInput'),
    methodSelect: $('methodSelect'),
    methodDescription: $('methodDescription'),
    fajrAngleInput: $('fajrAngleInput'),
    ishaAngleInput: $('ishaAngleInput'),
    asrSelect: $('asrSelect'),
    adjustFajr: $('adjustFajr'),
    adjustSunrise: $('adjustSunrise'),
    adjustDhuhr: $('adjustDhuhr'),
    adjustAsr: $('adjustAsr'),
    adjustMaghrib: $('adjustMaghrib'),
    adjustIsha: $('adjustIsha'),
    timetableEffectiveFrom: $('timetableEffectiveFrom'),
    jamaatNote: $('jamaatNote'),
    timetableStatus: $('timetableStatus'),
    conflictPanel: $('conflictPanel'),
    conflictList: $('conflictList'),
    flashWarningInput: $('flashWarningInput'),
    alertTable: $('alertTable'),
    warningFrame: $('warningFrame'),
    dialogDisclaimer: $('dialogDisclaimer'),
    footerNote: document.querySelector('.footer-note'),
    ambient: $('ambient'),
    ambientBlock: $('ambientBlock'),
    ambientTime: $('ambientTime'),
    ambientMeridiem: $('ambientMeridiem'),
    ambientDate: $('ambientDate'),
    ambientNext: $('ambientNext'),
    ambientTimes: $('ambientTimes'),
    ambientContent: $('ambientContent'),
    ambientArabic: $('ambientArabic'),
    ambientEnglish: $('ambientEnglish'),
    ambientRef: $('ambientRef'),
    ambientContentSelect: $('ambientContentSelect'),
    ambientEnabledInput: $('ambientEnabledInput'),
    focusBeforeInput: $('focusBeforeInput'),
    focusAfterInput: $('focusAfterInput'),
    nightEnabledInput: $('nightEnabledInput'),
    nightStartInput: $('nightStartInput'),
    ambientBrightnessInput: $('ambientBrightnessInput'),
    ambientBrightnessValue: $('ambientBrightnessValue'),
    nightBrightnessInput: $('nightBrightnessInput'),
    nightBrightnessValue: $('nightBrightnessValue'),
    previewAmbientButton: $('previewAmbientButton'),
    quranButton: $('quranButton'),
    hadithButton: $('hadithButton'),
    aodButton: $('aodButton'),
    contentStatus: $('contentStatus'),
    contentRefreshButton: $('contentRefreshButton'),
    libraryDialog: $('libraryDialog'),
    libraryTitle: $('libraryTitle'),
    libraryHint: $('libraryHint'),
    libraryList: $('libraryList'),
    libraryMore: $('libraryMore'),
    librarySearch: $('librarySearch'),
    libraryHiddenInput: $('libraryHiddenInput'),
    closeLibraryButton: $('closeLibraryButton')
  };

  const state = {
    data: null,
    view: null,
    serverOffsetMs: 0,
    lastFetch: 0,
    loading: false,
    retryTimer: 0,
    toastTimer: 0,
    rangeRequestToken: 0,
    ranges: null,
    settingsConflicts: [],
    flashWarning: true,
    alertKeys: null,
    formatters: new Map(),
    ambientPrefs: null,
    mode: null,
    wakeUntil: 0,
    previewUntil: 0,
    ambientSlot: null,
    ambientNextKey: '',
    ambientTimesKey: '',
    ambientMoveTimer: 0,
    activeContent: null,
    activeLoadedAt: 0,
    activeLoading: null,
    contentPick: null,
    contentKey: null, // null = unknown, so the first render always applies

    fitCache: new Map(),
    ambientPosition: null,
    aod: null,
    aodPressedAt: 0,
    alertPrefs: null,
    draftAlerts: null,
    theme: null,
    settingsDraft: null, // saved values to restore when the dialog is closed without saving
    settingsTab: 'location',
    timetableBaseline: '',
    previewFromSettings: false,
    settingsOpenedAt: 0,
    contentRefreshRequested: false,
    library: { type: 'quran', hidden: false, items: [], shown: 0, token: 0 },
    contentStatusTimer: 0
  };

  const SECOND = 1000;
  const MINUTE = 60 * SECOND;
  const REFRESH_MS = 10 * MINUTE;
  const WAKE_MS = 2 * MINUTE;
  const PREVIEW_MS = 20 * SECOND;
  const FLASH_STORAGE_KEY = 'deskDashboard.flashWarning';
  const ALERTS_STORAGE_KEY = 'deskDashboard.jamaatAlerts';
  const AMBIENT_STORAGE_KEY = 'deskDashboard.ambient';
  // Before 0.2.0 the alert preferences lived in the page; they now live in the database (so the phone can
  // plan notifications without the page) and are copied there once.
  const LEGACY_ALERT_PREFS_KEY = 'deskDashboard.jamaatAlertPrefs';
  const THEME_STORAGE_KEY = 'deskDashboard.theme';
  const PRAYER_KEYS = ['fajr', 'dhuhr', 'asr', 'maghrib', 'isha'];
  const PRAYER_NAMES = { fajr: 'Fajr', dhuhr: 'Dhuhr', asr: 'Asr', maghrib: 'Maghrib', isha: 'Isha' };
  const AMBIENT_MARGIN = 16;
  const LIBRARY_PAGE = 40;

  init();

  function init() {
    document.body.classList.toggle('is-native', isNativeHost);
    if (isNativeHost && elements.footerNote) {
      elements.footerNote.textContent = 'Runs on this device';
    }
    loadFlashPreference();
    state.alertPrefs = window.JamaatState.normalizeAlertPrefs(null);
    loadAmbientPrefs();
    loadTheme();
    bindEvents();
    applyBurnInDrift();
    // Start in the full view so the first load is visible; ambient takes over after WAKE_MS.
    state.wakeUntil = nowMs() + WAKE_MS;
    setInterval(tick, SECOND);
    setInterval(() => {
      if (!document.hidden) {
        loadToday();
      }
    }, REFRESH_MS);
    setInterval(applyBurnInDrift, MINUTE);
    loadToday();
    updateDisplayMode();
  }

  function nowMs() {
    return Date.now() + state.serverOffsetMs;
  }

  // Read-only snapshot for debugging on a device (WebView DevTools console).
  window.deskDashboardDebug = () => ({
    mode: state.mode,
    phase: state.view ? state.view.phase : null,
    now: nowMs(),
    wakeUntil: state.wakeUntil,
    previewUntil: state.previewUntil,
    serverOffsetMs: state.serverOffsetMs,
    prefs: state.ambientPrefs
  });

  function bindEvents() {
    elements.settingsButton.addEventListener('click', openSettings);
    elements.closeSettingsButton.addEventListener('click', discardSettings);
    elements.cancelSettingsButton.addEventListener('click', discardSettings);
    elements.saveSettingsButton.addEventListener('click', saveSettings);
    // Escape closes the dialog: treat it like Cancel.
    elements.settingsDialog.addEventListener('cancel', (event) => {
      event.preventDefault();
      discardSettings();
    });
    elements.settingsTabs.addEventListener('click', (event) => {
      const tab = event.target.closest('[data-tab]');
      // On a phone the tap that opened Settings can arrive again on the tab under the gear button.
      if (tab && Date.now() - state.settingsOpenedAt > 450) {
        selectSettingsTab(tab.dataset.tab);
      }
    });
    elements.settingsTabs.addEventListener('keydown', handleTabKeys);
    elements.settingsForm.addEventListener('input', markSettingsDirty);
    elements.settingsForm.addEventListener('change', markSettingsDirty);
    elements.flashWarningInput.addEventListener('change', handleFlashChange);
    elements.alertTable.addEventListener('change', handleAlertInput);
    elements.jamaatCards.addEventListener('change', handleJamaatCardInput);
    elements.jamaatCards.addEventListener('input', handleJamaatCardInput);
    elements.timetableEffectiveFrom.addEventListener('change', () => refreshTimetableLimits());
    elements.fontChoices.addEventListener('change', handleThemeInput);
    elements.accentChoices.addEventListener('change', handleThemeInput);
    elements.accentCustomInput.addEventListener('input', handleThemeInput);
    elements.searchButton.addEventListener('click', searchLocation);
    elements.searchInput.addEventListener('keydown', (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        searchLocation();
      }
    });
    elements.methodSelect.addEventListener('change', handleMethodChange);
    elements.settingsForm.addEventListener('submit', (event) => {
      event.preventDefault();
      saveSettings();
    });
    elements.settingsDialog.addEventListener('click', (event) => {
      if (event.target === elements.settingsDialog) {
        discardSettings();
      }
    });
    document.addEventListener('visibilitychange', () => {
      if (!document.hidden && Date.now() - state.lastFetch > MINUTE) {
        loadToday();
      }
    });
    window.addEventListener('online', loadToday);
    bindAmbientEvents();
  }

  async function loadToday() {
    if (state.loading) {
      return;
    }

    state.loading = true;
    window.clearTimeout(state.retryTimer);
    setConnection('syncing');

    try {
      const data = await request('/api/prayer/today');
      state.data = data;
      state.alertPrefs = window.JamaatState.normalizeAlertPrefs(data.alerts || null);
      state.serverOffsetMs = Date.parse(data.serverNowUtc) - Date.now();
      state.lastFetch = Date.now();
      hideBanner();
      render(data);
      setConnection('online');
    } catch (error) {
      console.error('Failed to load prayer data.', error);
      setConnection('offline');
      showBanner(isNativeHost
        ? 'Could not load prayer data on this device. Retrying…'
        : 'Cannot reach the dashboard server. Check that the PC is running and on the same Wi-Fi network.', 'error');
      scheduleRetry();
    } finally {
      state.loading = false;
    }
  }

  function scheduleRetry() {
    window.clearTimeout(state.retryTimer);
    state.retryTimer = window.setTimeout(() => {
      if (!document.hidden) {
        loadToday();
      }
    }, 15 * SECOND);
  }

  function render(data) {
    const cityLine = [data.location.city, data.location.country].filter(Boolean).join(', ');
    elements.locationLabel.textContent = cityLine || 'Unknown location';
    elements.clockZone.textContent = data.timeZoneId;

    renderClock();
    renderNext();
    renderPrayers(data);
    renderDashboardConflicts(data.conflicts);

    const method = data.methods.find((item) => item.key === data.settings.methodKey);
    elements.methodChip.textContent = method ? method.label : data.settings.methodKey;
    elements.disclaimer.textContent = data.disclaimer;
    elements.dialogDisclaimer.textContent = data.disclaimer;

    const synced = formatClockTime(new Date(Date.parse(data.serverNowUtc)), data.timeZoneId);
    elements.lastUpdated.textContent = `Synced ${synced.time} ${synced.meridiem}`;
    migrateLegacyAlertPrefs(data);
  }

  function renderClock() {
    const timeZone = state.data?.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone;
    const now = new Date(Date.now() + state.serverOffsetMs);
    const values = formatParts(getClockFormatter(timeZone), now);

    const hour24 = Number(values.hour) % 24;
    const hour12 = hour24 % 12 || 12;
    const meridiem = hour24 >= 12 ? 'PM' : 'AM';

    elements.clockMain.textContent = `${hour12}:${values.minute}`;
    elements.clockSeconds.textContent = values.second;
    elements.clockMeridiem.textContent = meridiem;
    elements.clockDate.textContent = getDateFormatter(timeZone).format(now);
  }

  function renderNext() {
    applyCountdownView();
  }

  function entriesFor(data) {
    const entries = [];
    if (data.previousIsha) {
      entries.push(data.previousIsha);
    }
    for (const time of data.times) {
      entries.push(time);
    }
    if (data.tomorrowFajr) {
      entries.push(data.tomorrowFajr);
    }
    return entries;
  }

  function applyCountdownView() {
    const data = state.data;
    if (!data || !window.JamaatState) {
      return;
    }

    const now = Date.now() + state.serverOffsetMs;
    const view = window.JamaatState.evaluate(entriesFor(data), now, {
      warningMinutes: window.JamaatState.warningMinutesOf(state.alertPrefs || {})
    });
    state.view = view;

    const prayer = view.prayer;
    const isWarning = view.phase === 'approaching';
    elements.nextPanel.classList.toggle('is-warning', isWarning);
    elements.nextPanel.classList.toggle('is-reached', view.phase === 'reached');
    elements.nextPanel.classList.toggle('is-passed', view.phase === 'passed');
    elements.warningChip.hidden = !isWarning;
    elements.warningNote.hidden = !isWarning;
    // Full-screen red frame and the red ambient block, in every display mode.
    elements.warningFrame.hidden = !isWarning;
    elements.ambientBlock.classList.toggle('is-warning', isWarning);

    if (!prayer) {
      elements.nextLabel.textContent = 'Prayer schedule';
      elements.nextName.textContent = '—';
      elements.nextBadge.hidden = true;
      elements.nextTime.textContent = '--:--';
      elements.nextMeridiem.textContent = '';
      elements.nextCaption.textContent = 'Updating the schedule…';
      elements.countdownLabel.textContent = 'Please wait';
      elements.countdown.textContent = '--:--:--';
      updatePrayerHighlight(null);
      return;
    }

    const timeZone = data.timeZoneId;
    const waqt = formatClockTime(new Date(prayer.at), timeZone);
    const jamaat = prayer.jamaatAt ? formatClockTime(new Date(prayer.jamaatAt), timeZone) : null;
    const eventDate = getDateKey(new Date(prayer.at), timeZone);
    const isTomorrow = eventDate !== data.date;

    elements.nextName.textContent = prayer.name;
    elements.nextLabel.textContent = labelForPhase(view.phase);
    elements.nextBadge.hidden = !(view.phase === 'upcoming' && isTomorrow);
    elements.countdown.textContent = '--:--:--';
    elements.warningChip.textContent = 'Jamaat soon';
    elements.warningNote.textContent = '';

    let shown = waqt;
    switch (view.phase) {
      case 'upcoming':
        elements.nextCaption.textContent = isTomorrow ? 'Tomorrow · calculated start' : 'Calculated start';
        elements.countdownLabel.textContent = 'Waqt starts in';
        elements.countdown.textContent = formatRemaining(view.remainingMs);
        break;
      case 'waiting':
        shown = jamaat || waqt;
        elements.nextCaption.textContent = 'Waqt has started';
        elements.countdownLabel.textContent = 'Jamaat in';
        elements.countdown.textContent = formatRemaining(view.remainingMs);
        break;
      case 'approaching': {
        shown = jamaat || waqt;
        const minutes = Math.max(1, Math.floor((view.remainingMs || 0) / MINUTE));
        elements.nextCaption.textContent = 'Waqt has started · Jamaat approaching';
        elements.countdownLabel.textContent = 'Jamaat in';
        elements.countdown.textContent = formatRemaining(view.remainingMs);
        elements.warningNote.textContent =
          `Jamaat is in ${minutes} minute${minutes === 1 ? '' : 's'}. Prepare for prayer.`;
        maybeAnnounceJamaat(view);
        break;
      }
      case 'reached':
        shown = jamaat || waqt;
        elements.nextCaption.textContent = 'Jamaat time has begun.';
        elements.countdownLabel.textContent = 'Jamaat';
        elements.countdown.textContent = 'Now';
        break;
      case 'passed':
        shown = jamaat || waqt;
        elements.nextCaption.textContent = 'Jamaat has passed. Waiting for the next prayer.';
        elements.countdownLabel.textContent = 'Jamaat';
        elements.countdown.textContent = 'Passed';
        break;
      case 'missing':
        elements.nextCaption.textContent = 'Waqt has started. Add a Jamaat time in settings.';
        elements.countdownLabel.textContent = view.remainingMs != null ? 'Next waqt in' : 'Jamaat';
        elements.countdown.textContent = view.remainingMs != null ? formatRemaining(view.remainingMs) : 'Not set';
        break;
      case 'invalid':
        elements.nextCaption.textContent =
          prayer.validationError || 'The stored Jamaat time is outside this prayer window.';
        elements.countdownLabel.textContent = view.remainingMs != null ? 'Next waqt in' : 'Jamaat';
        elements.countdown.textContent = view.remainingMs != null ? formatRemaining(view.remainingMs) : 'Check';
        break;
      default:
        break;
    }

    elements.nextTime.textContent = shown.time;
    elements.nextMeridiem.textContent = shown.meridiem;
    updatePrayerHighlight(view);
  }

  function labelForPhase(phase) {
    switch (phase) {
      case 'waiting':
        return 'Prayer in progress';
      case 'approaching':
        return 'Jamaat approaching';
      case 'reached':
        return 'Jamaat time';
      case 'passed':
        return 'Jamaat passed';
      case 'missing':
        return 'Jamaat not set';
      case 'invalid':
        return 'Check Jamaat time';
      default:
        return 'Next prayer';
    }
  }

  function formatRemaining(ms) {
    return window.JamaatState.splitDuration(ms).clock;
  }

  function updatePrayerHighlight(view) {
    const activeKey = view && view.prayer ? view.prayer.key : null;
    const warning = Boolean(view && view.phase === 'approaching');
    for (const row of elements.prayerList.children) {
      row.classList.toggle('is-next', row.dataset.prayerKey === activeKey);
      row.classList.toggle('is-warning', warning && row.dataset.prayerKey === activeKey);
    }
  }

  function maybeAnnounceJamaat(view) {
    if (!view.eventKey) {
      return;
    }

    if (!state.alertKeys) {
      try {
        state.alertKeys = readAlertKeys();
      } catch {
        state.alertKeys = [];
      }
    }

    if (state.alertKeys.includes(view.eventKey)) {
      return;
    }

    state.alertKeys.push(view.eventKey);
    state.alertKeys = state.alertKeys.slice(-24);
    try {
      window.localStorage.setItem(ALERTS_STORAGE_KEY, JSON.stringify(state.alertKeys));
    } catch {
      // The visual warning remains available when storage is blocked.
    }

    const minutes = Math.max(1, Math.floor((view.remainingMs || 0) / MINUTE));
    showToast(`${view.prayer.name} Jamaat in ${minutes} minute${minutes === 1 ? '' : 's'}.`);
  }

  function readAlertKeys() {
    const raw = window.localStorage.getItem(ALERTS_STORAGE_KEY);
    if (!raw) {
      return [];
    }

    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed.filter((item) => typeof item === 'string') : [];
  }

  function renderPrayers(data) {
    elements.prayerDate.textContent = formatDateLong(data.date);
    const list = elements.prayerList;
    list.replaceChildren();

    for (const prayer of data.times) {
      const row = document.createElement('div');
      row.className = 'prayer-row prayer-entry';
      row.setAttribute('role', 'row');
      row.dataset.prayerKey = prayer.key;
      if (prayer.key === 'sunrise') {
        row.classList.add('is-sunrise');
      }
      if (state.view?.prayer?.key === prayer.key) {
        row.classList.add('is-next');
      }
      if (state.view?.phase === 'approaching' && state.view.prayer.key === prayer.key) {
        row.classList.add('is-warning');
      }
      if (prayer.jamaatWithinWindow === false) {
        row.classList.add('is-invalid');
      }

      const nameCell = document.createElement('span');
      nameCell.className = 'prayer-name';
      nameCell.setAttribute('role', 'cell');
      const name = document.createElement('span');
      name.textContent = prayer.name;
      nameCell.append(name);
      if (prayer.key === 'sunrise') {
        const note = document.createElement('small');
        note.textContent = 'no jamaat';
        nameCell.append(note);
      }

      const calcCell = document.createElement('span');
      calcCell.className = 'calc-time';
      calcCell.setAttribute('role', 'cell');
      calcCell.textContent = formatTime12(prayer.time).label;

      const jamaatCell = document.createElement('span');
      jamaatCell.className = 'jamaat-time';
      jamaatCell.setAttribute('role', 'cell');
      if (prayer.jamaatTime) {
        jamaatCell.textContent = formatTime12(prayer.jamaatTime).label;
        if (prayer.jamaatWithinWindow === false) {
          jamaatCell.classList.add('is-invalid');
          jamaatCell.title = prayer.jamaatValidationError || 'Outside the allowed prayer window.';
          const flag = document.createElement('small');
          flag.className = 'jamaat-flag';
          flag.textContent = 'check';
          jamaatCell.append(' ', flag);
        } else if (prayer.jamaatAdjusted) {
          // The fixed time is outside today's window; the chosen fallback gave this time.
          jamaatCell.title = prayer.jamaatNote || 'Adjusted for today.';
          const flag = document.createElement('small');
          flag.className = 'jamaat-flag is-adjusted';
          flag.textContent = 'auto';
          jamaatCell.append(' ', flag);
        } else if (prayer.jamaatRuleText && prayer.jamaatMode !== 'fixed') {
          jamaatCell.title = prayer.jamaatRuleText;
        }
      } else {
        jamaatCell.classList.add('is-empty');
        jamaatCell.textContent = '—';
      }

      const diffCell = document.createElement('span');
      diffCell.setAttribute('role', 'cell');
      buildDifference(diffCell, prayer);

      row.append(nameCell, calcCell, jamaatCell, diffCell);
      list.append(row);
    }
  }

  function buildDifference(target, prayer) {
    if (prayer.jamaatWithinWindow === false) {
      target.className = 'diff diff-invalid';
      target.textContent = 'Check';
      target.title = prayer.jamaatValidationError || 'The Jamaat time is outside the allowed prayer window.';
      return;
    }

    if (!prayer.jamaatTime || prayer.differenceMinutes === null || prayer.differenceMinutes === undefined) {
      target.className = 'diff diff-none';
      target.textContent = '—';
      return;
    }

    const minutes = prayer.differenceMinutes;
    if (minutes === 0) {
      target.className = 'diff diff-same';
      target.textContent = 'Same';
      target.title = 'The Jamaat time matches the calculated start time.';
      return;
    }

    target.className = minutes > 0 ? 'diff diff-later' : 'diff diff-earlier';
    const value = document.createElement('span');
    value.textContent = `${minutes > 0 ? '+' : ''}${minutes}`;
    const unit = document.createElement('small');
    unit.textContent = 'min';
    target.append(value, unit);
    target.title = minutes > 0
      ? `The mosque Jamaat is ${minutes} minutes after the calculated start.`
      : `The mosque Jamaat is ${Math.abs(minutes)} minutes before the calculated start. Verify this with the mosque.`;
  }

  function tick() {
    if (!state.data) {
      renderClock();
      updateDisplayMode();
      return;
    }

    renderClock();
    applyCountdownView();
    updateDisplayMode();
    syncIfDateChanged();
  }

  function syncIfDateChanged() {
    if (!state.data || Date.now() - state.lastFetch < MINUTE) {
      return;
    }

    const currentDate = getDateKey(new Date(Date.now() + state.serverOffsetMs), state.data.timeZoneId);
    if (currentDate !== state.data.date) {
      loadToday();
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Settings dialog: tabs, one Save. While it is open everything is a draft; display and theme
  // changes preview live and are put back by Cancel.
  // ---------------------------------------------------------------------------------------------

  function openSettings() {
    if (!state.data) {
      showToast('Still connecting to the server.');
      return;
    }

    // Coming back from "Preview the always-on view": keep the draft as it was.
    if (state.settingsDraft && state.previewFromSettings) {
      state.previewFromSettings = false;
      if (!elements.settingsDialog.open) {
        elements.settingsDialog.showModal();
      }
      return;
    }

    const data = state.data;
    state.settingsOpenedAt = Date.now();
    state.settingsDraft = {
      ambientPrefs: { ...state.ambientPrefs },
      flashWarning: state.flashWarning,
      theme: { ...state.theme }
    };
    populateMethodSelect(data.methods, data.settings.methodKey);
    fillSettingsForm(data.settings);
    state.ranges = data;
    fillTimetableForm(data.timetable, data.date);
    state.timetableBaseline = JSON.stringify(timetablePayload());
    elements.settingsErrors.hidden = true;
    elements.timetableStatus.textContent = '';
    elements.searchStatus.textContent = '';
    elements.searchResults.hidden = true;
    elements.searchResults.replaceChildren();
    elements.dialogDisclaimer.textContent = data.disclaimer;
    elements.flashWarningInput.checked = state.flashWarning;
    state.draftAlerts = window.JamaatState.normalizeAlertPrefs(state.alertPrefs);
    renderAlertRows();
    fillAmbientInputs();
    renderThemeChoices();
    renderConflictPanel(data.conflicts || []);
    clearTabErrors();
    elements.settingsDirty.textContent = '';
    selectSettingsTab(state.settingsTab || 'location');

    if (!elements.settingsDialog.open) {
      elements.settingsDialog.showModal();
    }

    loadSettingsConflicts();
    loadContentStatus();
  }

  /** Cancel, ×, Escape or a tap outside: put back what was saved and close. */
  function discardSettings() {
    const draft = state.settingsDraft;
    state.settingsDraft = null;
    state.previewFromSettings = false;
    if (draft) {
      state.ambientPrefs = draft.ambientPrefs;
      state.flashWarning = draft.flashWarning;
      state.theme = draft.theme;
      applyFlashPreference();
      applyTheme(state.theme);
      state.contentKey = null;
      state.contentPick = null;
      state.ambientSlot = null;
      state.mode = null; // re-apply brightness
    }
    if (elements.settingsDialog.open) {
      elements.settingsDialog.close();
    }
    updateDisplayMode();
  }

  function markSettingsDirty() {
    if (state.settingsDraft) {
      elements.settingsDirty.textContent = 'Unsaved changes';
    }
  }

  function selectSettingsTab(name) {
    const tabs = [...elements.settingsTabs.querySelectorAll('[data-tab]')];
    if (!tabs.some((tab) => tab.dataset.tab === name)) {
      name = 'location';
    }
    state.settingsTab = name;
    for (const tab of tabs) {
      const selected = tab.dataset.tab === name;
      tab.setAttribute('aria-selected', String(selected));
      tab.tabIndex = selected ? 0 : -1;
    }
    for (const panel of elements.settingsDialog.querySelectorAll('[data-panel]')) {
      panel.hidden = panel.dataset.panel !== name;
    }
    const body = elements.settingsDialog.querySelector('.settings-panels');
    if (body) {
      body.scrollTop = 0;
    }
  }

  function handleTabKeys(event) {
    if (!['ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
      return;
    }
    const tabs = [...elements.settingsTabs.querySelectorAll('[data-tab]')];
    const index = tabs.findIndex((tab) => tab.dataset.tab === state.settingsTab);
    let next = index;
    if (event.key === 'Home') {
      next = 0;
    } else if (event.key === 'End') {
      next = tabs.length - 1;
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowRight') {
      next = (index + 1) % tabs.length;
    } else {
      next = (index - 1 + tabs.length) % tabs.length;
    }
    event.preventDefault();
    selectSettingsTab(tabs[next].dataset.tab);
    tabs[next].focus();
  }

  function clearTabErrors() {
    for (const tab of elements.settingsTabs.querySelectorAll('[data-tab]')) {
      tab.classList.remove('has-error');
    }
    for (const control of elements.settingsDialog.querySelectorAll('.is-invalid')) {
      control.classList.remove('is-invalid');
    }
  }

  // Which tab an API error key belongs to ("country", "timetable.asr", "alerts.fajr" …).
  function tabForErrorKey(key) {
    const lower = String(key || '').toLowerCase();
    if (lower.startsWith('timetable.') || lower === 'timetable') {
      return 'jamaat';
    }
    if (lower.startsWith('alerts.')) {
      return 'alerts';
    }
    if (['country', 'city', 'latitude', 'longitude', 'timezoneid'].includes(lower)) {
      return 'location';
    }
    return 'prayer';
  }

  function markTabErrors(keys) {
    const tabs = new Set(keys.map(tabForErrorKey));
    for (const tab of elements.settingsTabs.querySelectorAll('[data-tab]')) {
      tab.classList.toggle('has-error', tabs.has(tab.dataset.tab));
    }
    const first = elements.settingsTabs.querySelector('.has-error');
    if (first) {
      selectSettingsTab(first.dataset.tab);
    }
  }

  async function loadSettingsConflicts() {
    try {
      const response = await request('/api/prayer/settings');
      state.settingsConflicts = response?.conflicts || [];
      renderConflictPanel(state.settingsConflicts);
    } catch (error) {
      console.warn('Could not load the stored Jamaat conflicts.', error);
    }
  }

  function populateMethodSelect(methods, selectedKey) {
    elements.methodSelect.replaceChildren();
    for (const method of methods) {
      const option = document.createElement('option');
      option.value = method.key;
      option.textContent = method.label;
      if (method.key === selectedKey) {
        option.selected = true;
      }
      elements.methodSelect.append(option);
    }
    updateMethodDescription();
  }

  function fillSettingsForm(settings) {
    elements.countryInput.value = settings.country;
    elements.cityInput.value = settings.city;
    elements.latitudeInput.value = settings.latitude;
    elements.longitudeInput.value = settings.longitude;
    elements.timeZoneInput.value = settings.timeZoneId;
    elements.methodSelect.value = settings.methodKey;
    elements.fajrAngleInput.value = settings.fajrAngle;
    elements.ishaAngleInput.value = settings.ishaAngle;
    elements.asrSelect.value = settings.asrMadhab;
    elements.adjustFajr.value = settings.fajrAdjustment;
    elements.adjustSunrise.value = settings.sunriseAdjustment;
    elements.adjustDhuhr.value = settings.dhuhrAdjustment;
    elements.adjustAsr.value = settings.asrAdjustment;
    elements.adjustMaghrib.value = settings.maghribAdjustment;
    elements.adjustIsha.value = settings.ishaAdjustment;
    updateMethodDescription();
  }

  // ---------------------------------------------------------------------------------------------
  // Jamaat tab: one card per prayer with the method (fixed / after Waqt / rounded slot)
  // ---------------------------------------------------------------------------------------------

  function fillTimetableForm(timetable, todayDate) {
    const reference = state.ranges || state.data;
    elements.timetableEffectiveFrom.value = timetable?.effectiveFrom || todayDate;
    elements.jamaatNote.value = timetable?.note || '';
    elements.jamaatCards.replaceChildren();
    for (const key of PRAYER_KEYS) {
      const rule = window.JamaatRules.normalizeRule(timetable?.rules?.[key]);
      // A new timetable starts from the calculated Waqt, which the user then edits.
      const time = timetable ? (timetable[key] || '') : suggestedStart(reference, key);
      elements.jamaatCards.append(buildJamaatCard(key, rule, time));
    }
    applyTimetableLimits(reference);
  }

  function option(value, label, selected) {
    const item = document.createElement('option');
    item.value = value;
    item.textContent = label;
    item.selected = selected;
    return item;
  }

  function numberField(labelText, field, value, min, max, hint) {
    const label = document.createElement('label');
    label.className = 'field';
    const text = document.createElement('span');
    text.textContent = labelText;
    const input = document.createElement('input');
    input.type = 'number';
    input.min = String(min);
    input.max = String(max);
    input.step = '1';
    input.inputMode = 'numeric';
    input.dataset.field = field;
    input.value = String(value);
    label.append(text, input);
    if (hint) {
      const small = document.createElement('small');
      small.className = 'field-hint';
      small.textContent = hint;
      label.append(small);
    }
    return label;
  }

  function slotField(field, value) {
    const label = document.createElement('label');
    label.className = 'field';
    const text = document.createElement('span');
    text.textContent = 'Round to every';
    const select = document.createElement('select');
    select.dataset.field = field;
    for (const size of window.JamaatRules.SLOT_SIZES) {
      select.append(option(String(size), `${size} minutes`, size === value));
    }
    label.append(text, select);
    return label;
  }

  function buildJamaatCard(key, rule, time) {
    const card = document.createElement('div');
    card.className = 'jamaat-card';
    card.dataset.prayerKey = key;
    const fallback = rule.fallback || { mode: 'offset', minutes: 10 };

    const head = document.createElement('div');
    head.className = 'jamaat-card-head';
    const name = document.createElement('strong');
    name.textContent = PRAYER_NAMES[key];
    const preview = document.createElement('span');
    preview.className = 'jamaat-preview';
    preview.dataset.role = 'preview';
    head.append(name, preview);

    const modeLabel = document.createElement('label');
    modeLabel.className = 'field';
    const modeText = document.createElement('span');
    modeText.textContent = 'Jamaat time is';
    const mode = document.createElement('select');
    mode.dataset.field = 'mode';
    mode.append(
      option('fixed', 'A fixed time', rule.mode === 'fixed'),
      option('offset', 'Minutes after the Waqt', rule.mode === 'offset'),
      option('slot', 'The next rounded time after the Waqt', rule.mode === 'slot'));
    modeLabel.append(modeText, mode);

    // Fixed time and what happens when the Waqt passes it.
    const fixed = document.createElement('div');
    fixed.className = 'jamaat-mode field-grid';
    fixed.dataset.mode = 'fixed';
    const timeLabel = document.createElement('label');
    timeLabel.className = 'field';
    const timeText = document.createElement('span');
    timeText.textContent = 'Time';
    const timeInput = document.createElement('input');
    timeInput.type = 'time';
    timeInput.step = '60';
    timeInput.dataset.field = 'time';
    timeInput.value = time || '';
    const range = document.createElement('small');
    range.className = 'field-hint';
    range.dataset.role = 'range';
    timeLabel.append(timeText, timeInput, range);

    const fallbackLabel = document.createElement('label');
    fallbackLabel.className = 'field';
    const fallbackText = document.createElement('span');
    fallbackText.textContent = 'If the Waqt moves past this time';
    const fallbackMode = document.createElement('select');
    fallbackMode.dataset.field = 'fallbackMode';
    fallbackMode.append(
      option('offset', 'Use the Waqt plus some minutes', fallback.mode === 'offset'),
      option('slot', 'Use the next rounded time', fallback.mode === 'slot'),
      option('warn', 'Keep my time and warn me', fallback.mode === 'warn'));
    fallbackLabel.append(fallbackText, fallbackMode);

    const fallbackOffset = document.createElement('div');
    fallbackOffset.className = 'jamaat-sub';
    fallbackOffset.dataset.fallback = 'offset';
    fallbackOffset.append(numberField('Minutes after the Waqt', 'fallbackMinutes', fallback.minutes ?? 10, 0, 120));

    const fallbackSlot = document.createElement('div');
    fallbackSlot.className = 'jamaat-sub field-grid';
    fallbackSlot.dataset.fallback = 'slot';
    fallbackSlot.append(
      slotField('fallbackSlot', fallback.slot ?? 15),
      numberField('At least (min after Waqt)', 'fallbackGap', fallback.gap ?? 5, 0, 60));

    fixed.append(timeLabel, fallbackLabel, fallbackOffset, fallbackSlot);

    const offset = document.createElement('div');
    offset.className = 'jamaat-mode field-grid';
    offset.dataset.mode = 'offset';
    offset.append(numberField('Minutes after the Waqt', 'minutes', rule.minutes ?? 5, 0, 120,
      'The Jamaat follows the Waqt every day.'));

    const slot = document.createElement('div');
    slot.className = 'jamaat-mode field-grid';
    slot.dataset.mode = 'slot';
    slot.append(
      slotField('slot', rule.slot ?? 15),
      numberField('At least (min after Waqt)', 'gap', rule.gap ?? 5, 0, 60,
        'For example every 15 minutes: …:00, :15, :30, :45.'));

    const note = document.createElement('p');
    note.className = 'section-hint jamaat-note';
    note.dataset.role = 'note';

    card.append(head, modeLabel, fixed, offset, slot, note);
    syncJamaatCard(card);
    return card;
  }

  function readJamaatCard(card) {
    const value = (field) => card.querySelector(`[data-field="${field}"]`)?.value ?? '';
    const mode = value('mode');
    let rule;
    if (mode === 'offset') {
      rule = { mode: 'offset', minutes: Number(value('minutes')) };
    } else if (mode === 'slot') {
      rule = { mode: 'slot', slot: Number(value('slot')), gap: Number(value('gap')) };
    } else {
      const fallbackMode = value('fallbackMode');
      const fallback = fallbackMode === 'slot'
        ? { mode: 'slot', slot: Number(value('fallbackSlot')), gap: Number(value('fallbackGap')) }
        : fallbackMode === 'warn'
          ? { mode: 'warn' }
          : { mode: 'offset', minutes: Number(value('fallbackMinutes')) };
      rule = { mode: 'fixed', fallback };
    }
    return { key: card.dataset.prayerKey, time: value('time'), rule };
  }

  // Shows only the fields of the chosen method, and the preview for the reference date.
  function syncJamaatCard(card) {
    const entry = readJamaatCard(card);
    for (const block of card.querySelectorAll('[data-mode]')) {
      block.hidden = block.dataset.mode !== entry.rule.mode;
    }
    const fallbackMode = entry.rule.fallback ? entry.rule.fallback.mode : '';
    for (const block of card.querySelectorAll('[data-fallback]')) {
      block.hidden = block.dataset.fallback !== fallbackMode;
    }

    const preview = card.querySelector('[data-role="preview"]');
    const note = card.querySelector('[data-role="note"]');
    const reference = state.ranges || state.data;
    const prayer = reference?.times?.find((item) => item.key === entry.key);
    const span = prayer ? prayerWindow(prayer) : null;
    const Rules = window.JamaatRules;
    note.textContent = '';
    note.classList.remove('is-warning');
    if (!span) {
      preview.textContent = '';
      return;
    }

    const result = Rules.resolve(entry.rule, entry.time, span);
    const dateLabel = reference.date === state.data?.date ? 'Today' : formatDateShort(reference.date);
    const waqt = Rules.formatTime(span.start);
    if (!result) {
      preview.textContent = `${dateLabel}: no Jamaat set (Waqt ${waqt})`;
      return;
    }
    preview.textContent = `${dateLabel}: ${Rules.formatTime(result.at)} (Waqt ${waqt})`;
    if (result.adjusted) {
      note.textContent = `Your time ${Rules.formatTime(Rules.parseTime(entry.time))} is outside the window on this date, ` +
        `so ${Rules.formatTime(result.at)} is used (${Rules.describe(entry.rule.fallback)}).`;
    } else if (!result.valid) {
      note.textContent = 'This time is outside the prayer window on this date. It will be shown with a warning.';
      note.classList.add('is-warning');
    } else if (result.capped) {
      note.textContent = 'That would be after the prayer window, so the last minute of the window is used.';
    }
  }

  // The window for a prayer from the API ("HH:mm" earliest / latest), in minutes of the day.
  function prayerWindow(prayer) {
    return window.JamaatRules.windowFrom(prayer.jamaatEarliest, prayer.jamaatLatest);
  }

  function handleJamaatCardInput(event) {
    const card = event.target.closest('.jamaat-card');
    if (card) {
      event.target.classList.remove('is-invalid');
      syncJamaatCard(card);
    }
  }

  function applyTimetableLimits(reference) {
    if (!reference?.times) {
      return;
    }
    for (const card of elements.jamaatCards.querySelectorAll('.jamaat-card')) {
      const prayer = reference.times.find((item) => item.key === card.dataset.prayerKey);
      const hint = card.querySelector('[data-role="range"]');
      if (hint) {
        hint.textContent = prayer?.jamaatRange
          ? `Allowed ${prayer.jamaatRange} · Waqt ${formatTime12(prayer.time).label}`
          : 'Allowed window unavailable';
      }
      syncJamaatCard(card);
    }
  }

  async function refreshTimetableLimits() {
    const date = elements.timetableEffectiveFrom.value || state.data?.date;
    if (!date) {
      return;
    }

    const token = ++state.rangeRequestToken;
    elements.timetableStatus.textContent = 'Loading prayer windows…';

    try {
      const reference = await request(`/api/prayer/today?date=${encodeURIComponent(date)}`);
      if (token !== state.rangeRequestToken) {
        return;
      }

      state.ranges = reference;
      applyTimetableLimits(reference);
      elements.timetableStatus.textContent = `Previews and windows for ${formatDateShort(date)}.`;
    } catch (error) {
      if (token !== state.rangeRequestToken) {
        return;
      }

      console.warn('Could not load timetable windows.', error);
      elements.timetableStatus.textContent =
        'Could not load the windows for that date. The times are still checked when you save.';
    }
  }

  function suggestedStart(reference, key) {
    const prayer = reference?.times?.find((item) => item.key === key);
    return prayer?.time || '';
  }

  function timetablePayload() {
    const payload = {
      effectiveFrom: elements.timetableEffectiveFrom.value || null,
      note: elements.jamaatNote.value.trim() || null,
      rules: {}
    };
    for (const card of elements.jamaatCards.querySelectorAll('.jamaat-card')) {
      const entry = readJamaatCard(card);
      payload[entry.key] = entry.time || null;
      payload.rules[entry.key] = window.JamaatRules.normalizeRule(entry.rule);
    }
    return payload;
  }

  // A fixed time must fit its window on the effective date (the other methods always do).
  function validateJamaatCards() {
    const errors = {};
    const reference = state.ranges || state.data;
    for (const card of elements.jamaatCards.querySelectorAll('.jamaat-card')) {
      const entry = readJamaatCard(card);
      const input = card.querySelector('[data-field="time"]');
      input.classList.remove('is-invalid');
      if (entry.rule.mode !== 'fixed' || !entry.time) {
        continue;
      }
      const prayer = reference?.times?.find((item) => item.key === entry.key);
      const span = prayer ? prayerWindow(prayer) : null;
      const result = span ? window.JamaatRules.resolve({ mode: 'fixed', fallback: { mode: 'warn' } }, entry.time, span) : null;
      if (result && !result.valid) {
        input.classList.add('is-invalid');
        errors[`timetable.${entry.key}`] = [
          `${PRAYER_NAMES[entry.key]} Jamaat is outside the prayer window on the effective date. ` +
          `Allowed ${prayer.jamaatRange}.`
        ];
      }
    }
    return errors;
  }

  function renderConflictPanel(conflicts) {
    elements.conflictList.replaceChildren();
    if (!conflicts || conflicts.length === 0) {
      elements.conflictPanel.hidden = true;
      return;
    }

    for (const conflict of conflicts.slice(0, 12)) {
      const item = document.createElement('li');
      item.textContent =
        `${conflict.prayerName}: ${conflict.jamaatTime} on ${formatDateShort(conflict.onDate)} ` +
        `is outside ${conflict.allowedRange}.`;
      elements.conflictList.append(item);
    }

    if (conflicts.length > 12) {
      const item = document.createElement('li');
      item.textContent = `And ${conflicts.length - 12} more conflicts.`;
      elements.conflictList.append(item);
    }

    elements.conflictPanel.hidden = false;
  }

  function renderDashboardConflicts(conflicts) {
    if (!conflicts || conflicts.length === 0) {
      return;
    }

    const first = conflicts[0];
    const extra = conflicts.length > 1 ? ` (+${conflicts.length - 1} more)` : '';
    showBanner(
      `Jamaat time conflict: ${first.prayerName} ${first.jamaatTime} on ${formatDateShort(first.onDate)} ` +
      `is outside ${first.allowedRange}${extra}. Open settings to correct it.`,
      'warning'
    );
  }

  function handleMethodChange() {
    const method = getSelectedMethod();
    if (!method) {
      return;
    }

    if (method.key !== 'Custom') {
      elements.fajrAngleInput.value = method.defaultFajrAngle;
      elements.ishaAngleInput.value = method.defaultIshaAngle;
    }
    updateMethodDescription();
  }

  function updateMethodDescription() {
    const method = getSelectedMethod();
    if (!method) {
      elements.methodDescription.textContent = '';
      return;
    }

    const fajr = elements.fajrAngleInput.value || method.defaultFajrAngle;
    const isha = elements.ishaAngleInput.value || method.defaultIshaAngle;
    elements.methodDescription.textContent =
      `${method.description} Selected angles: Fajr ${fajr}°, Isha ${isha}°. ` +
      'Adjust the angles only if you know they suit your mosque.';
  }

  function getSelectedMethod() {
    return state.data?.methods.find((item) => item.key === elements.methodSelect.value) ?? null;
  }

  async function searchLocation() {
    const query = elements.searchInput.value.trim();
    if (query.length < 2) {
      elements.searchStatus.textContent = 'Enter at least 2 characters.';
      elements.searchResults.hidden = true;
      return;
    }

    elements.searchButton.disabled = true;
    elements.searchStatus.textContent = 'Searching…';
    elements.searchResults.hidden = true;
    elements.searchResults.replaceChildren();

    try {
      const results = await request(`/api/location/search?q=${encodeURIComponent(query)}`);
      if (!Array.isArray(results) || results.length === 0) {
        elements.searchStatus.textContent = 'No matches found. Enter coordinates manually.';
        return;
      }

      elements.searchStatus.textContent = 'Select a result to fill the location fields.';
      for (const result of results) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'search-result';

        const text = document.createElement('span');
        const title = document.createElement('span');
        title.textContent = result.label || result.name;
        const sub = document.createElement('small');
        sub.textContent = [result.timeZoneId, `${result.latitude.toFixed(4)}, ${result.longitude.toFixed(4)}`]
          .filter(Boolean)
          .join(' • ');
        text.append(title, sub);
        button.append(text);
        button.addEventListener('click', () => applyLocationResult(result));

        elements.searchResults.append(button);
      }
      elements.searchResults.hidden = false;
    } catch (error) {
      elements.searchStatus.textContent = errorMessage(error);
    } finally {
      elements.searchButton.disabled = false;
    }
  }

  function applyLocationResult(result) {
    elements.countryInput.value = result.country || '';
    elements.cityInput.value = result.name || '';
    elements.latitudeInput.value = result.latitude;
    elements.longitudeInput.value = result.longitude;
    if (result.timeZoneId) {
      elements.timeZoneInput.value = result.timeZoneId;
    }
    elements.searchStatus.textContent = `Using ${result.label || result.name}. Save settings to apply.`;
    elements.searchResults.hidden = true;
  }

  /** The one Save: prayer settings, Jamaat timetable (when changed) and alerts in one request. */
  async function saveSettings() {
    clearTabErrors();
    elements.settingsErrors.hidden = true;

    const invalid = settingsFieldControls().find((control) => !control.checkValidity());
    if (invalid) {
      const panel = invalid.closest('[data-panel]');
      if (panel) {
        selectSettingsTab(panel.dataset.panel);
      }
      const details = invalid.closest('details');
      if (details) {
        details.open = true;
      }
      invalid.reportValidity();
      return;
    }

    const timetable = timetablePayload();
    const timetableChanged = JSON.stringify(timetable) !== state.timetableBaseline;
    const clientErrors = timetableChanged ? validateJamaatCards() : {};
    if (timetableChanged && !timetable.effectiveFrom) {
      clientErrors['timetable.effectiveFrom'] = ['Choose the date this Jamaat timetable becomes effective.'];
    }
    if (Object.keys(clientErrors).length > 0) {
      showFormErrors(elements.settingsErrors, { payload: { errors: clientErrors } });
      markTabErrors(Object.keys(clientErrors));
      return;
    }

    const payload = {
      prayer: {
        country: elements.countryInput.value.trim(),
        city: elements.cityInput.value.trim(),
        latitude: Number(elements.latitudeInput.value),
        longitude: Number(elements.longitudeInput.value),
        timeZoneId: elements.timeZoneInput.value.trim(),
        methodKey: elements.methodSelect.value,
        fajrAngle: Number(elements.fajrAngleInput.value),
        ishaAngle: Number(elements.ishaAngleInput.value),
        asrMadhab: elements.asrSelect.value,
        fajrAdjustment: readInteger(elements.adjustFajr.value),
        sunriseAdjustment: readInteger(elements.adjustSunrise.value),
        dhuhrAdjustment: readInteger(elements.adjustDhuhr.value),
        asrAdjustment: readInteger(elements.adjustAsr.value),
        maghribAdjustment: readInteger(elements.adjustMaghrib.value),
        ishaAdjustment: readInteger(elements.adjustIsha.value)
      },
      timetable: timetableChanged ? timetable : null,
      alerts: state.draftAlerts
    };

    elements.saveSettingsButton.disabled = true;
    elements.settingsDirty.textContent = 'Saving…';

    try {
      const response = await request('/api/settings', {
        method: 'PUT',
        body: JSON.stringify(payload)
      });

      // The page-only preferences are kept once the server has accepted everything.
      saveAmbientPrefs();
      saveFlashPreference();
      saveTheme();
      state.settingsDraft = null;
      state.alertPrefs = window.JamaatState.normalizeAlertPrefs(response?.alerts || state.draftAlerts);
      state.settingsConflicts = response?.conflicts || [];
      elements.settingsDialog.close();
      await loadToday();
      requestNotificationPermission();

      const count = state.settingsConflicts.length;
      showToast(count > 0
        ? `Saved. ${count} Jamaat time${count === 1 ? '' : 's'} need review in Settings → Jamaat.`
        : 'Settings saved.');
    } catch (error) {
      elements.settingsDirty.textContent = 'Not saved';
      showFormErrors(elements.settingsErrors, error);
      markTabErrors(Object.keys(error?.payload?.errors || {}));
    } finally {
      elements.saveSettingsButton.disabled = false;
    }
  }

  function showFormErrors(container, error) {
    const messages = errorMessages(error);
    const list = document.createElement('ul');
    for (const message of messages) {
      const item = document.createElement('li');
      item.textContent = message;
      list.append(item);
    }
    container.replaceChildren(list);
    container.hidden = false;
  }

  function errorMessages(error) {
    const payload = error?.payload;
    if (payload?.errors && typeof payload.errors === 'object') {
      return Object.values(payload.errors).flat().filter(Boolean);
    }
    if (payload?.detail) {
      return [payload.detail];
    }
    if (payload?.title) {
      return [payload.title];
    }
    return [error?.message || 'The request failed.'];
  }

  function errorMessage(error) {
    return errorMessages(error)[0];
  }

  function readInteger(value) {
    const number = Number.parseInt(value, 10);
    return Number.isFinite(number) ? number : 0;
  }

  function settingsFieldControls() {
    return [
      elements.countryInput,
      elements.cityInput,
      elements.latitudeInput,
      elements.longitudeInput,
      elements.timeZoneInput,
      elements.methodSelect,
      elements.fajrAngleInput,
      elements.ishaAngleInput,
      elements.asrSelect,
      elements.adjustFajr,
      elements.adjustSunrise,
      elements.adjustDhuhr,
      elements.adjustAsr,
      elements.adjustMaghrib,
      elements.adjustIsha
    ].filter(Boolean);
  }

  function loadNativeBridge() {
    if (!nativeBridge) {
      nativeBridge = window.HybridWebView
        ? Promise.resolve(window.HybridWebView)
        : new Promise((resolve, reject) => {
          const script = document.createElement('script');
          script.src = '/_framework/hybridwebview.js';
          script.onload = () => (window.HybridWebView
            ? resolve(window.HybridWebView)
            : reject(new Error('The app bridge did not load.')));
          script.onerror = () => reject(new Error('The app bridge did not load.'));
          document.head.appendChild(script);
        });
      nativeBridge.catch(() => { nativeBridge = null; });
    }
    return nativeBridge;
  }

  async function nativeRequest(url, options) {
    const bridge = await loadNativeBridge();
    const raw = await bridge.InvokeDotNet('Api', [
      (options.method || 'GET').toUpperCase(),
      url,
      typeof options.body === 'string' ? options.body : null
    ]);
    const response = typeof raw === 'string' ? JSON.parse(raw) : raw;
    const payload = response?.body ? JSON.parse(response.body) : null;
    if (!response || response.status < 200 || response.status >= 300) {
      const error = new Error(`Request failed with status ${response?.status ?? 'unknown'}.`);
      error.payload = payload;
      throw error;
    }
    return payload;
  }

  async function request(url, options = {}) {
    if (isNativeHost) {
      return nativeRequest(url, options);
    }

    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 8000);
    const headers = { Accept: 'application/json' };
    if (options.body) {
      headers['Content-Type'] = 'application/json';
    }

    try {
      const response = await fetch(url, {
        ...options,
        headers: { ...headers, ...(options.headers || {}) },
        cache: 'no-store',
        signal: controller.signal
      });

      let payload = null;
      const text = await response.text();
      if (text) {
        try {
          payload = JSON.parse(text);
        } catch {
          payload = null;
        }
      }

      if (!response.ok) {
        const error = new Error(`Request failed with status ${response.status}.`);
        error.payload = payload;
        throw error;
      }

      return payload;
    } catch (error) {
      if (error.name === 'AbortError') {
        const timeoutError = new Error('The server did not respond in time.');
        timeoutError.payload = null;
        throw timeoutError;
      }
      throw error;
    } finally {
      window.clearTimeout(timeout);
    }
  }

  function setConnection(kind) {
    elements.connection.classList.toggle('is-online', kind === 'online');
    elements.connection.classList.toggle('is-offline', kind === 'offline');
    elements.statusText.textContent = kind === 'online'
      ? 'Live'
      : kind === 'offline'
        ? 'Offline'
        : 'Syncing';
  }

  function showBanner(message, kind = 'error') {
    elements.banner.textContent = message;
    elements.banner.classList.toggle('is-warning', kind === 'warning');
    elements.banner.hidden = false;
  }

  function hideBanner() {
    elements.banner.hidden = true;
    elements.banner.classList.remove('is-warning');
  }

  function loadFlashPreference() {
    try {
      state.flashWarning = window.localStorage.getItem(FLASH_STORAGE_KEY) !== '0';
    } catch {
      state.flashWarning = true;
    }
    applyFlashPreference();
  }

  // Applies at once (preview); kept only when the settings are saved.
  function handleFlashChange() {
    state.flashWarning = elements.flashWarningInput.checked;
    applyFlashPreference();
  }

  function saveFlashPreference() {
    try {
      window.localStorage.setItem(FLASH_STORAGE_KEY, state.flashWarning ? '1' : '0');
    } catch {
      // The preference applies for this session even when storage is blocked.
    }
  }

  function applyFlashPreference() {
    document.body.classList.toggle('no-warning-flash', !state.flashWarning);
  }

  // ---------------------------------------------------------------------------------------------
  // Jamaat alerts: warning minutes and notifications per prayer. Stored in the database, so the phone
  // plans its notifications without this page (see JamaatAlertPlanner in Core).
  // ---------------------------------------------------------------------------------------------

  // Copies alert preferences from before 0.2.0 (kept in this page) into the database, once.
  function migrateLegacyAlertPrefs(data) {
    let legacy = null;
    try {
      legacy = window.localStorage.getItem(LEGACY_ALERT_PREFS_KEY);
    } catch {
      return;
    }
    if (!legacy || state.migratingAlerts) {
      return;
    }
    state.migratingAlerts = true;
    let prefs;
    try {
      prefs = window.JamaatState.normalizeAlertPrefs(JSON.parse(legacy));
    } catch {
      prefs = null;
    }
    const done = () => {
      try {
        window.localStorage.removeItem(LEGACY_ALERT_PREFS_KEY);
      } catch {
        // Nothing more to do.
      }
    };
    if (!prefs) {
      done();
      return;
    }
    request('/api/settings', {
      method: 'PUT',
      body: JSON.stringify({ prayer: data.settings, timetable: null, alerts: prefs })
    })
      .then(() => {
        done();
        state.alertPrefs = prefs;
      })
      .catch((error) => console.warn('Could not move the alert preferences.', error))
      .finally(() => { state.migratingAlerts = false; });
  }

  // Today's Waqt-to-Jamaat time for a prayer, in whole minutes, or null without a valid Jamaat.
  function jamaatGapMinutes(key) {
    const time = state.data && state.data.times.find((item) => item.key === key);
    if (!time || !time.jamaatAt || time.jamaatWithinWindow === false) {
      return null;
    }
    const gap = Math.floor((Date.parse(time.jamaatAt) - Date.parse(time.at)) / MINUTE);
    return gap > 0 ? gap : null;
  }

  function renderAlertRows() {
    const table = elements.alertTable;
    for (const row of table.querySelectorAll('.alert-entry')) {
      row.remove();
    }
    const prefsAll = state.draftAlerts || state.alertPrefs;
    const timeZone = (state.data && state.data.timeZoneId) || Intl.DateTimeFormat().resolvedOptions().timeZone;
    for (const key of PRAYER_KEYS) {
      const prefs = prefsAll[key];
      const time = state.data && state.data.times.find((item) => item.key === key);
      const name = (time && time.name) || PRAYER_NAMES[key];
      const gap = jamaatGapMinutes(key);

      const row = document.createElement('div');
      row.className = 'alert-row alert-entry';
      row.setAttribute('role', 'row');
      row.dataset.prayerKey = key;

      const label = document.createElement('span');
      label.className = 'alert-name';
      label.setAttribute('role', 'cell');
      label.textContent = name;

      const notifyCell = document.createElement('label');
      notifyCell.className = 'alert-notify';
      notifyCell.setAttribute('role', 'cell');
      const notify = document.createElement('input');
      notify.type = 'checkbox';
      notify.dataset.field = 'notify';
      notify.checked = prefs.notify;
      notify.setAttribute('aria-label', `${name} notification`);
      const notifyText = document.createElement('span');
      notifyText.textContent = 'On';
      notifyCell.append(notify, notifyText);

      const minutesCell = document.createElement('div');
      minutesCell.className = 'alert-minutes';
      minutesCell.setAttribute('role', 'cell');
      const minutes = document.createElement('input');
      minutes.type = 'number';
      minutes.min = '1';
      minutes.max = '120';
      minutes.step = '1';
      minutes.inputMode = 'numeric';
      minutes.dataset.field = 'minutes';
      minutes.value = String(prefs.minutes);
      minutes.setAttribute('aria-label', `${name}: minutes before Jamaat`);
      const hint = document.createElement('small');
      hint.className = 'field-hint';
      if (gap && time) {
        const waqt = formatClockTime(new Date(time.at), timeZone);
        const jamaat = formatClockTime(new Date(time.jamaatAt), timeZone);
        hint.textContent = `Today: Waqt ${waqt.time} → Jamaat ${jamaat.time} (${gap} min)`;
        if (prefs.minutes > gap) {
          hint.textContent += `. Starts at the Waqt today.`;
        }
      } else {
        hint.textContent = 'No Jamaat time set';
      }
      minutesCell.append(minutes, hint);

      row.append(label, notifyCell, minutesCell);
      table.appendChild(row);
    }
  }

  function handleAlertInput(event) {
    const row = event.target.closest('.alert-entry');
    if (!row) {
      return;
    }
    const key = row.dataset.prayerKey;
    const next = { ...state.draftAlerts };
    next[key] = {
      notify: row.querySelector('[data-field="notify"]').checked,
      minutes: Number(row.querySelector('[data-field="minutes"]').value)
    };
    state.draftAlerts = window.JamaatState.normalizeAlertPrefs(next);
    renderAlertRows();
  }

  // Android 13+ asks once before an app may show notifications.
  function requestNotificationPermission() {
    if (!isNativeHost || !PRAYER_KEYS.some((key) => state.alertPrefs?.[key]?.notify)) {
      return;
    }
    loadNativeBridge()
      .then((bridge) => bridge.InvokeDotNet('EnsureNotificationPermission', []))
      .catch((error) => console.warn('Could not ask for the notification permission.', error));
  }

  // ---------------------------------------------------------------------------------------------
  // Appearance: clock font and accent colour (ThemeState)
  // ---------------------------------------------------------------------------------------------

  function loadTheme() {
    let raw = null;
    try {
      raw = JSON.parse(window.localStorage.getItem(THEME_STORAGE_KEY) || 'null');
    } catch {
      raw = null;
    }
    state.theme = window.ThemeState.normalize(raw);
    applyTheme(state.theme);
  }

  function saveTheme() {
    try {
      window.localStorage.setItem(THEME_STORAGE_KEY, JSON.stringify(state.theme));
    } catch {
      // Applies for this session.
    }
  }

  function applyTheme(theme) {
    const root = document.documentElement.style;
    const vars = window.ThemeState.palette(theme.accent);
    for (const name of Object.keys(vars)) {
      root.setProperty(name, vars[name]);
    }
    const font = window.ThemeState.fontFor(theme.font);
    root.setProperty('--clock-font', font.family);
    root.setProperty('--clock-weight', String(font.weight));
    document.documentElement.dataset.clockFont = font.key;
    // Element sizes can change with the font: measure the ambient content again.
    state.fitCache = new Map();
  }

  function renderThemeChoices() {
    const theme = state.theme;
    elements.fontChoices.replaceChildren();
    for (const font of window.ThemeState.FONTS) {
      const label = document.createElement('label');
      label.className = 'font-choice';
      const input = document.createElement('input');
      input.type = 'radio';
      input.name = 'clockFont';
      input.value = font.key;
      input.checked = font.key === theme.font;
      const sample = document.createElement('span');
      sample.className = 'font-sample';
      sample.style.fontFamily = font.family;
      sample.style.fontWeight = String(font.weight);
      sample.textContent = '4:35';
      const name = document.createElement('small');
      name.textContent = font.label;
      label.append(input, sample, name);
      elements.fontChoices.append(label);
    }

    elements.accentChoices.replaceChildren();
    const preset = window.ThemeState.ACCENTS.find((item) => item.hex === theme.accent);
    for (const accent of window.ThemeState.ACCENTS) {
      const label = document.createElement('label');
      label.className = 'accent-choice';
      label.title = accent.label;
      const input = document.createElement('input');
      input.type = 'radio';
      input.name = 'accent';
      input.value = accent.hex;
      input.checked = accent.hex === theme.accent;
      input.setAttribute('aria-label', accent.label);
      const swatch = document.createElement('span');
      swatch.className = 'accent-swatch';
      swatch.style.background = accent.hex;
      label.append(input, swatch);
      elements.accentChoices.append(label);
    }
    elements.accentCustomInput.value = theme.accent;
    elements.accentCustomInput.closest('.accent-custom').classList.toggle('is-active', !preset);
  }

  function handleThemeInput(event) {
    const next = { ...state.theme };
    if (event.target.name === 'clockFont') {
      next.font = event.target.value;
    } else if (event.target.name === 'accent' || event.target === elements.accentCustomInput) {
      next.accent = event.target.value;
    }
    state.theme = window.ThemeState.normalize(next);
    applyTheme(state.theme);
    if (event.target === elements.accentCustomInput) {
      for (const input of elements.accentChoices.querySelectorAll('input')) {
        input.checked = input.value === state.theme.accent;
      }
      elements.accentCustomInput.closest('.accent-custom').classList.add('is-active');
    } else if (event.target.name === 'accent') {
      elements.accentCustomInput.value = state.theme.accent;
      elements.accentCustomInput.closest('.accent-custom').classList.remove('is-active');
    }
  }

  function showToast(message) {
    elements.toast.textContent = message;
    elements.toast.hidden = false;
    window.clearTimeout(state.toastTimer);
    state.toastTimer = window.setTimeout(() => {
      elements.toast.hidden = true;
    }, 3000);
  }

  function formatTime12(value) {
    const match = /^(\d{1,2}):(\d{2})/.exec(value || '');
    if (!match) {
      return { time: value || '—', meridiem: '', label: value || '—' };
    }

    const hour24 = Number(match[1]) % 24;
    const hour12 = hour24 % 12 || 12;
    const meridiem = hour24 >= 12 ? 'PM' : 'AM';
    const time = `${hour12}:${match[2]}`;
    return { time, meridiem, label: `${time} ${meridiem}` };
  }

  function formatClockTime(date, timeZone) {
    const values = formatParts(getClockFormatter(timeZone), date);
    const hour24 = Number(values.hour) % 24;
    return {
      time: `${hour24 % 12 || 12}:${values.minute}`,
      meridiem: hour24 >= 12 ? 'PM' : 'AM'
    };
  }

  function formatDateLong(dateText) {
    const date = new Date(`${dateText}T12:00:00Z`);
    return new Intl.DateTimeFormat('en-GB', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC'
    }).format(date);
  }

  function formatDateShort(dateText) {
    const date = new Date(`${dateText}T12:00:00Z`);
    if (Number.isNaN(date.getTime())) {
      return dateText;
    }

    return new Intl.DateTimeFormat('en-GB', {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC'
    }).format(date);
  }

  function getDateKey(date, timeZone) {
    const values = formatParts(new Intl.DateTimeFormat('en-CA', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      timeZone
    }), date);
    return `${values.year}-${values.month}-${values.day}`;
  }

  function formatParts(formatter, date) {
    const values = {};
    for (const part of formatter.formatToParts(date)) {
      values[part.type] = part.value;
    }
    return values;
  }

  function getClockFormatter(timeZone) {
    const key = `clock:${timeZone}`;
    if (!state.formatters.has(key)) {
      state.formatters.set(key, new Intl.DateTimeFormat('en-GB', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
        hourCycle: 'h23',
        timeZone
      }));
    }
    return state.formatters.get(key);
  }

  function getDateFormatter(timeZone) {
    const key = `date:${timeZone}`;
    if (!state.formatters.has(key)) {
      state.formatters.set(key, new Intl.DateTimeFormat('en-GB', {
        weekday: 'long',
        day: 'numeric',
        month: 'long',
        year: 'numeric',
        timeZone
      }));
    }
    return state.formatters.get(key);
  }

  function applyBurnInDrift() {
    const slot = Math.floor(Date.now() / (5 * MINUTE));
    const x = ((slot * 7) % 5) - 2;
    const y = ((slot * 3) % 5) - 2;
    elements.clockInner.style.setProperty('--drift-x', `${x}px`);
    elements.clockInner.style.setProperty('--drift-y', `${y}px`);

    // The whole full-view page also shifts by up to ±3 px each minute.
    const minuteSlot = Math.floor(Date.now() / MINUTE);
    const pageX = ((minuteSlot * 5) % 7) - 3;
    const pageY = ((minuteSlot * 3) % 7) - 3;
    document.body.style.setProperty('--page-drift-x', `${pageX}px`);
    document.body.style.setProperty('--page-drift-y', `${pageY}px`);
  }

  // ---------------------------------------------------------------------------------------------
  // Ambient / night display modes
  // ---------------------------------------------------------------------------------------------

  function loadAmbientPrefs() {
    let raw = null;
    try {
      raw = JSON.parse(window.localStorage.getItem(AMBIENT_STORAGE_KEY) || 'null');
    } catch {
      raw = null;
    }
    // Ambient mode is on by default in the phone app, off in a PC/desktop browser.
    state.ambientPrefs = window.AmbientState
      ? window.AmbientState.normalizePrefs(raw, { enabled: isNativeHost })
      : { enabled: false };
  }

  function saveAmbientPrefs() {
    try {
      window.localStorage.setItem(AMBIENT_STORAGE_KEY, JSON.stringify(state.ambientPrefs));
    } catch {
      // Preferences still apply for this session.
    }
  }

  function bindAmbientEvents() {
    elements.ambient.addEventListener('click', () => {
      // The tap that pressed AOD can arrive here as a second (ghost) click once the ambient
      // screen covers the button; don't let it wake the dashboard straight away.
      if (Date.now() - state.aodPressedAt < 800) {
        return;
      }
      state.previewUntil = 0;
      wake();
    });

    // Any interaction keeps the full view up a little longer.
    for (const type of ['pointerdown', 'keydown', 'scroll']) {
      document.addEventListener(type, () => {
        if (state.mode === 'focus') {
          state.wakeUntil = Math.max(state.wakeUntil, nowMs() + WAKE_MS);
        }
      }, { passive: true, capture: true });
    }

    elements.settingsDialog.addEventListener('close', () => {
      state.wakeUntil = nowMs() + WAKE_MS;
      updateDisplayMode();
    });

    const inputs = [
      elements.ambientEnabledInput,
      elements.focusBeforeInput,
      elements.focusAfterInput,
      elements.nightEnabledInput,
      elements.nightStartInput,
      elements.ambientBrightnessInput,
      elements.nightBrightnessInput,
      elements.ambientContentSelect
    ];
    for (const input of inputs) {
      input.addEventListener('change', handleAmbientInput);
      input.addEventListener('input', updateBrightnessLabels);
    }

    elements.previewAmbientButton.addEventListener('click', () => {
      // Shows the draft (not yet saved) display settings; Settings comes back afterwards, unchanged.
      state.previewFromSettings = Boolean(state.settingsDraft);
      elements.settingsDialog.close();
      state.wakeUntil = 0;
      state.previewUntil = Date.now() + PREVIEW_MS;
      updateDisplayMode();
      showToast('Always-on preview for 20 seconds. Tap to return to Settings.');
      window.setTimeout(returnFromPreview, PREVIEW_MS + 200);
    });

    window.addEventListener('resize', () => {
      state.ambientSlot = null;
      // A different screen size can change which items fit: choose again for this minute.
      state.contentPick = null;
      if (state.mode && state.mode !== 'focus') {
        renderAmbient(nowMs(), true);
      }
    });

    elements.aodButton.addEventListener('click', enterAod);
    elements.quranButton.addEventListener('click', () => openLibrary('quran'));
    elements.hadithButton.addEventListener('click', () => openLibrary('hadith'));
    bindLibraryEvents();
    elements.contentRefreshButton.addEventListener('click', startContentRefresh);
    watchAmbientSize();
  }

  function wake() {
    state.aod = null;
    state.wakeUntil = nowMs() + WAKE_MS;
    updateDisplayMode();
    returnFromPreview();
  }

  function returnFromPreview() {
    if (state.previewFromSettings && state.settingsDraft) {
      state.previewUntil = 0;
      openSettings();
    }
  }

  function fillAmbientInputs() {
    const prefs = state.ambientPrefs;
    elements.ambientEnabledInput.checked = prefs.enabled;
    elements.focusBeforeInput.value = prefs.focusBeforeMinutes;
    elements.focusAfterInput.value = prefs.focusAfterMinutes;
    elements.nightEnabledInput.checked = prefs.nightEnabled;
    elements.nightStartInput.value = prefs.nightStart.padStart(5, '0');
    elements.ambientBrightnessInput.value = Math.round(prefs.ambientBrightness * 100);
    elements.nightBrightnessInput.value = Math.round(prefs.nightBrightness * 100);
    elements.ambientContentSelect.value = prefs.content;
    updateBrightnessLabels();
  }

  function updateBrightnessLabels() {
    elements.ambientBrightnessValue.textContent = `${elements.ambientBrightnessInput.value}%`;
    elements.nightBrightnessValue.textContent = `${elements.nightBrightnessInput.value}%`;
  }

  function handleAmbientInput() {
    if (!window.AmbientState) {
      return;
    }
    state.ambientPrefs = window.AmbientState.normalizePrefs({
      enabled: elements.ambientEnabledInput.checked,
      focusBeforeMinutes: elements.focusBeforeInput.value,
      focusAfterMinutes: elements.focusAfterInput.value,
      nightEnabled: elements.nightEnabledInput.checked,
      nightStart: elements.nightStartInput.value,
      ambientBrightness: Number(elements.ambientBrightnessInput.value) / 100,
      nightBrightness: Number(elements.nightBrightnessInput.value) / 100,
      content: elements.ambientContentSelect.value
    }, { enabled: isNativeHost });
    // null (not '') so the next render always re-applies, even when it now shows no content.
    state.contentKey = null;
    state.contentPick = null;
    state.ambientSlot = null;
    fillAmbientInputs();
    state.mode = null; // re-apply brightness with the new values
    updateDisplayMode();
  }

  function minutesOfDayIn(ms, timeZone) {
    const values = formatParts(getClockFormatter(timeZone), new Date(ms));
    return (Number(values.hour) % 24) * 60 + Number(values.minute);
  }

  function updateDisplayMode() {
    const Ambient = window.AmbientState;
    if (!Ambient || !state.ambientPrefs) {
      return;
    }

    const now = nowMs();
    let mode = 'focus';
    if (state.previewUntil > Date.now()) {
      mode = 'ambient';
    } else if (state.data) {
      const timeZone = state.data.timeZoneId;
      const decision = Ambient.decideMode({
        now,
        prefs: state.ambientPrefs,
        entries: entriesFor(state.data),
        phase: state.view ? state.view.phase : null,
        wakeUntil: state.wakeUntil,
        forceFocus: elements.settingsDialog.open || elements.libraryDialog.open,
        aod: state.aod,
        minutesOfDay: (ms) => minutesOfDayIn(ms, timeZone)
      });
      mode = decision.mode;
      if (decision.aodEnded) {
        state.aod = null;
      }
    } else if (state.aod) {
      mode = 'ambient';
    }
    elements.aodButton.setAttribute('aria-pressed', String(Boolean(state.aod) && mode !== 'focus'));

    if (mode !== state.mode) {
      const previous = state.mode;
      state.mode = mode;
      document.body.classList.toggle('mode-focus', mode === 'focus');
      document.body.classList.toggle('mode-ambient', mode === 'ambient');
      document.body.classList.toggle('mode-night', mode === 'night');
      elements.ambient.hidden = mode === 'focus';
      setNativeBrightness(Ambient.brightnessFor(mode, state.ambientPrefs));
      if (previous === 'focus' || previous === null) {
        state.ambientSlot = null;
      }
    }

    if (mode !== 'focus') {
      renderAmbient(now, false);
    }
  }

  function setNativeBrightness(level) {
    if (!isNativeHost) {
      return;
    }
    loadNativeBridge()
      .then((bridge) => bridge.InvokeDotNet('SetBrightness', [level]))
      .catch((error) => console.warn('Brightness change failed.', error));
  }

  function renderAmbient(now, forceMove) {
    const data = state.data;
    const timeZone = data?.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone;
    const clock = formatClockTime(new Date(now), timeZone);
    setText(elements.ambientTime, clock.time);
    setText(elements.ambientMeridiem, clock.meridiem);
    setText(elements.ambientDate, getDateFormatter(timeZone).format(new Date(now)));
    renderAmbientNext(now, timeZone);
    renderAmbientTimes();
    const content = prepareAmbientContent(now);
    positionAmbient(now, forceMove, content);
  }

  // The Ayat/Hadith that may appear (not removed by the user), from the app's database.
  // Reloaded every 10 minutes so weekly additions and Library changes are picked up.
  function ensureActiveContent(force) {
    // An empty list (first launch, starter content still importing) is retried after 20 seconds.
    const maxAge = state.activeContent && state.activeContent.length ? 10 * MINUTE : 20 * SECOND;
    const fresh = state.activeContent && Date.now() - state.activeLoadedAt < maxAge;
    if ((!force && fresh) || state.activeLoading) {
      return;
    }
    state.activeLoading = request('/api/content/active')
      .then((items) => {
        // Keep a newer local "shown" time if the server has not recorded it yet.
        const shownLocally = new Map((state.activeContent || []).map((item) => [item.id, item.lastShownAt]));
        state.activeContent = (Array.isArray(items) ? items : []).map((item) => {
          const local = shownLocally.get(item.id);
          const server = item.lastShownAt;
          const lastShownAt = typeof local === 'number' && (typeof server !== 'number' || local > server) ? local : server;
          return { ...item, lastShownAt: typeof lastShownAt === 'number' ? lastShownAt : null };
        });
        state.activeLoadedAt = Date.now();
        if (state.mode === 'ambient' && !(state.contentPick && state.contentPick.item)) {
          state.contentPick = null;
          renderAmbient(nowMs(), true);
        }
      })
      .catch((error) => console.warn('Ayah/Hadith content is unavailable.', error))
      .finally(() => {
        state.activeLoading = null;
      });
  }

  // This minute's Ayah/Hadith: the least recently shown one that fits the screen completely.
  function selectContent(slot, choice) {
    const Ambient = window.AmbientState;
    const list = state.activeContent || [];
    const hasQuran = list.some((item) => item.type === 'quran');
    const hasHadith = list.some((item) => item.type === 'hadith');
    const type = Ambient.typeForSlot(slot, choice, hasQuran, hasHadith);
    if (!type) {
      return null;
    }
    const tooLong = (candidate) => measureFit(candidate) < 0;
    let item = Ambient.chooseNext(list, type, tooLong);
    if (!item && choice === 'both') {
      item = Ambient.chooseNext(list, type === 'quran' ? 'hadith' : 'quran', tooLong);
    }
    return item;
  }

  // Holy text is never shown partly. Measures an item in an invisible copy of the ambient block at
  // the current screen size: 0, 1 or 2 = the fit step it needs, -1 = it cannot fit here.
  function measureFit(item) {
    const key = `${item.id}@${elements.ambient.clientWidth}x${elements.ambient.clientHeight}`;
    if (state.fitCache.has(key)) {
      return state.fitCache.get(key);
    }
    if (elements.ambient.hidden || !elements.ambient.clientHeight) {
      return 0; // cannot measure yet; placement fits it again once visible
    }

    const probe = elements.ambientBlock.cloneNode(true);
    probe.removeAttribute('id');
    for (const node of probe.querySelectorAll('[id]')) {
      node.removeAttribute('id');
    }
    probe.style.cssText = 'position:absolute;left:0;top:0;transform:none;visibility:hidden;pointer-events:none;';
    probe.classList.add('has-content');
    probe.classList.remove('fit-1', 'fit-2');
    const content = probe.querySelector('.ambient-content');
    content.hidden = false;
    content.classList.toggle('is-long', item.arabic.length + item.english.length > 380);
    probe.querySelector('.ambient-arabic').textContent = item.arabic;
    probe.querySelector('.ambient-english').textContent = item.english;
    probe.querySelector('.ambient-ref').textContent = item.grade ? `${item.ref} · ${item.grade}` : item.ref;
    elements.ambient.appendChild(probe);

    const available = elements.ambient.clientHeight - 2 * AMBIENT_MARGIN;
    let level = -1;
    if (probe.offsetHeight <= available) {
      level = 0;
    } else {
      probe.classList.add('fit-1');
      if (probe.offsetHeight <= available) {
        level = 1;
      } else {
        probe.classList.add('fit-2');
        level = probe.offsetHeight <= available ? 2 : -1;
      }
    }
    probe.remove();

    // Only remember results measured with the final fonts.
    if (!document.fonts || document.fonts.status === 'loaded') {
      state.fitCache.set(key, level);
    }
    return level;
  }

  function markShown(item) {
    item.lastShownAt = Date.now();
    request(`/api/content/${item.id}/shown`, { method: 'POST' })
      .catch((error) => console.warn('Could not record the shown item.', error));
  }

  // Works out this minute's Ayah/Hadith. Returns { changed, apply }: positionAmbient calls
  // apply() while the block is faded out, before measuring it, so text swaps are never seen
  // mid-fade and the new size is used for placement.
  function prepareAmbientContent(now) {
    const choice = state.ambientPrefs.content;
    const showContent = state.mode === 'ambient' && choice !== 'off';
    if (showContent) {
      ensureActiveContent(false);
    }

    const slot = Math.floor(now / MINUTE);
    let pick = null;
    if (showContent && state.activeContent) {
      if (state.contentPick && state.contentPick.slot === slot && state.contentPick.choice === choice) {
        pick = state.contentPick.item;
      } else {
        pick = selectContent(slot, choice);
        state.contentPick = { slot, choice, item: pick };
        if (pick) {
          markShown(pick);
        }
      }
    }

    const key = pick ? String(pick.id) : '';
    if (key === state.contentKey) {
      return { changed: false, apply: () => {} };
    }
    state.contentKey = key;

    return {
      changed: true,
      apply: () => {
        elements.ambientBlock.classList.toggle('has-content', Boolean(pick));
        elements.ambientContent.hidden = !pick;
        if (!pick) {
          return;
        }
        elements.ambientArabic.textContent = pick.arabic;
        elements.ambientEnglish.textContent = pick.english;
        elements.ambientRef.textContent = pick.grade ? `${pick.ref} · ${pick.grade}` : pick.ref;
        elements.ambientContent.classList.toggle('is-long', pick.arabic.length + pick.english.length > 380);
        elements.ambientContent.dataset.type = pick.type;
      }
    };
  }

  function setText(element, text) {
    if (element.textContent !== text) {
      element.textContent = text;
    }
  }

  function formatShortDuration(ms) {
    const minutes = Math.ceil(Math.max(0, ms) / MINUTE);
    if (minutes < 1) {
      return 'now';
    }
    const hours = Math.floor(minutes / 60);
    const rest = minutes % 60;
    return hours > 0 ? `${hours}h ${rest}m` : `${rest}m`;
  }

  function renderAmbientNext(now, timeZone) {
    const view = state.view;
    let title = '';
    let detail = '';

    if (view && view.prayer) {
      const prayer = view.prayer;
      const waqt = formatClockTime(new Date(prayer.at), timeZone);
      const jamaat = prayer.jamaatAt ? formatClockTime(new Date(prayer.jamaatAt), timeZone) : null;
      const remaining = view.remainingMs != null ? formatShortDuration(view.remainingMs) : null;
      switch (view.phase) {
        case 'upcoming':
          title = prayer.name;
          detail = ` ${waqt.time} ${waqt.meridiem}${remaining ? ` · in ${remaining}` : ''}`;
          break;
        case 'waiting':
          title = `${prayer.name} Jamaat`;
          detail = ` ${jamaat.time} ${jamaat.meridiem}${remaining ? ` · in ${remaining}` : ''}`;
          break;
        case 'approaching': {
          // The warning: time left to the second, in red (see .ambient-block.is-warning).
          const left = window.JamaatState.splitDuration(view.remainingMs || 0);
          const clock = left.hours > 0
            ? left.clock
            : `${String(left.minutes).padStart(2, '0')}:${String(left.seconds).padStart(2, '0')}`;
          title = `${prayer.name} Jamaat in ${clock}`;
          detail = ` · ${jamaat.time} ${jamaat.meridiem}`;
          break;
        }
        case 'reached':
          title = `${prayer.name} Jamaat`;
          detail = ' now';
          break;
        case 'passed':
          title = `${prayer.name} Jamaat`;
          detail = ' passed';
          break;
        case 'missing':
        case 'invalid':
          title = prayer.name;
          detail = view.phase === 'missing' ? ' · Jamaat not set' : ' · check Jamaat time';
          break;
        default:
          break;
      }
    }

    const key = `${title}|${detail}`;
    if (key === state.ambientNextKey) {
      return;
    }
    state.ambientNextKey = key;
    const strong = document.createElement('strong');
    strong.textContent = title;
    elements.ambientNext.replaceChildren(strong, document.createTextNode(detail));
  }

  function renderAmbientTimes() {
    const data = state.data;
    if (!data) {
      return;
    }
    const activeKey = state.view?.prayer?.key || '';
    const prayers = data.times.filter((time) => time.isPrayer);
    const key = `${data.date}|${activeKey}|${prayers.map((p) => `${p.key}${p.jamaatTime || p.time}`).join(',')}`;
    if (key === state.ambientTimesKey) {
      return;
    }
    state.ambientTimesKey = key;

    const cells = [];
    for (const prayer of prayers) {
      const name = document.createElement('span');
      name.textContent = prayer.name;
      name.classList.toggle('is-active', prayer.key === activeKey);
      cells.push(name);
    }
    for (const prayer of prayers) {
      const time = document.createElement('span');
      const shown = formatTime12(prayer.jamaatTime || prayer.time);
      time.textContent = shown.label;
      time.title = prayer.jamaatTime ? 'Jamaat' : 'Calculated start';
      time.classList.toggle('is-active', prayer.key === activeKey);
      cells.push(time);
    }
    elements.ambientTimes.style.gridTemplateColumns = `repeat(${prayers.length}, auto)`;
    elements.ambientTimes.replaceChildren(...cells);
  }

  // Long Ayah/Hadith on a short screen: step down until the block fits.
  // fit-1 hides the date and times row and shrinks the clock; fit-2 also shrinks the text.
  function fitAmbientBlock() {
    const block = elements.ambientBlock;
    const available = elements.ambient.clientHeight - 2 * AMBIENT_MARGIN;
    block.classList.remove('fit-1', 'fit-2');
    if (block.offsetHeight <= available) {
      return;
    }
    block.classList.add('fit-1');
    if (block.offsetHeight <= available) {
      return;
    }
    block.classList.add('fit-2');
  }

  // Puts the block at fractions (fx, fy) of the free space, always fully inside the screen.
  function placeAmbient(position) {
    state.ambientPosition = position;
    fitAmbientBlock();
    const freeX = Math.max(0, elements.ambient.clientWidth - elements.ambientBlock.offsetWidth - 2 * AMBIENT_MARGIN);
    const freeY = Math.max(0, elements.ambient.clientHeight - elements.ambientBlock.offsetHeight - 2 * AMBIENT_MARGIN);
    const x = Math.round(AMBIENT_MARGIN + position.fx * freeX);
    const y = Math.round(AMBIENT_MARGIN + position.fy * freeY);
    elements.ambientBlock.style.transform = `translate(${x}px, ${y}px)`;
  }

  // If the block changes size after placement (a font finishing loading, a wrapped line), fit and
  // place it again immediately, so no line is ever pushed off the screen.
  function watchAmbientSize() {
    if (window.ResizeObserver) {
      let lastHeight = 0;
      new ResizeObserver(() => {
        const height = elements.ambientBlock.offsetHeight;
        if (height !== lastHeight && state.ambientPosition && state.mode !== 'focus' &&
            !elements.ambient.classList.contains('is-moving')) {
          lastHeight = height;
          placeAmbient(state.ambientPosition);
        }
      }).observe(elements.ambientBlock);
    }
    if (document.fonts && document.fonts.ready) {
      document.fonts.ready.then(() => {
        state.fitCache.clear();
        if (state.ambientPosition && state.mode !== 'focus') {
          placeAmbient(state.ambientPosition);
        }
      });
    }
  }

  function positionAmbient(now, forceMove, content) {
    const position = window.AmbientState.blockPosition(now);
    if (!forceMove && state.ambientSlot === position.slot) {
      if (content.changed) {
        // Content changed within the same minute (list loaded, setting changed): re-place now.
        content.apply();
        forceMove = true;
      } else {
        return;
      }
    }
    const firstPlacement = state.ambientSlot === null;
    state.ambientSlot = position.slot;

    window.clearTimeout(state.ambientMoveTimer);
    if (firstPlacement || forceMove) {
      content.apply();
      elements.ambient.classList.remove('is-moving');
      placeAmbient(position);
      return;
    }

    // Fade out, swap content, jump, fade in, so the change is calm rather than a visible slide.
    elements.ambient.classList.add('is-moving');
    state.ambientMoveTimer = window.setTimeout(() => {
      content.apply();
      placeAmbient(position);
      elements.ambient.classList.remove('is-moving');
    }, 1200);
  }

  // ---------------------------------------------------------------------------------------------
  // AOD button: ambient mode right away
  // ---------------------------------------------------------------------------------------------

  function enterAod() {
    const Ambient = window.AmbientState;
    const now = nowMs();
    let skipUntil = now;
    if (state.data && Ambient) {
      const active = Ambient.activeWindow(now, entriesFor(state.data), state.ambientPrefs);
      if (active) {
        skipUntil = active.end;
      }
    }
    const view = state.view;
    if (view && (view.phase === 'approaching' || view.phase === 'reached') && view.prayer && view.prayer.jamaatAt) {
      skipUntil = Math.max(skipUntil, view.prayer.jamaatAt + window.JamaatState.PASSED_GRACE_MS);
    }
    state.aod = { skipUntil };
    state.aodPressedAt = Date.now();
    state.wakeUntil = 0;
    state.previewUntil = 0;
    updateDisplayMode();
  }

  // ---------------------------------------------------------------------------------------------
  // Library: read every Ayah/Hadith, most recently shown first; remove or restore
  // ---------------------------------------------------------------------------------------------

  function bindLibraryEvents() {
    elements.closeLibraryButton.addEventListener('click', () => elements.libraryDialog.close());
    elements.libraryDialog.addEventListener('close', () => {
      state.wakeUntil = nowMs() + WAKE_MS;
      ensureActiveContent(true);
      updateDisplayMode();
    });
    for (const tab of document.querySelectorAll('[data-library-type]')) {
      tab.addEventListener('click', () => {
        state.library.type = tab.dataset.libraryType;
        loadLibrary();
      });
    }
    elements.libraryHiddenInput.addEventListener('change', () => {
      state.library.hidden = elements.libraryHiddenInput.checked;
      loadLibrary();
    });
    elements.librarySearch.addEventListener('input', () => renderLibrary(true));
    elements.libraryMore.addEventListener('click', () => renderLibrary(false));
  }

  function openLibrary(type) {
    state.library.type = type;
    state.library.hidden = false;
    elements.libraryHiddenInput.checked = false;
    elements.librarySearch.value = '';
    if (!elements.libraryDialog.open) {
      elements.libraryDialog.showModal();
    }
    updateDisplayMode();
    loadLibrary();
  }

  async function loadLibrary() {
    const library = state.library;
    const token = ++library.token;
    for (const tab of document.querySelectorAll('[data-library-type]')) {
      tab.setAttribute('aria-selected', String(tab.dataset.libraryType === library.type));
    }
    elements.libraryTitle.textContent = library.type === 'quran' ? 'Quran verses' : 'Hadith';
    elements.libraryHint.textContent = 'Loading…';
    elements.libraryList.replaceChildren();
    elements.libraryMore.hidden = true;

    try {
      const items = await request(`/api/content?type=${library.type}&hidden=${library.hidden}`);
      if (token !== library.token) {
        return;
      }
      library.items = Array.isArray(items) ? items : [];
      renderLibrary(true);
    } catch (error) {
      if (token === library.token) {
        elements.libraryHint.textContent = 'The library could not be loaded. Try again.';
      }
      console.warn('Library load failed.', error);
    }
  }

  function filteredLibrary() {
    const query = elements.librarySearch.value.trim().toLowerCase();
    if (!query) {
      return state.library.items;
    }
    return state.library.items.filter((item) =>
      item.ref.toLowerCase().includes(query) ||
      item.english.toLowerCase().includes(query) ||
      item.arabic.includes(query));
  }

  function renderLibrary(reset) {
    const library = state.library;
    if (reset) {
      elements.libraryList.replaceChildren();
      library.shown = 0;
    }
    const items = filteredLibrary();
    const page = items.slice(library.shown, library.shown + LIBRARY_PAGE);
    for (const item of page) {
      elements.libraryList.appendChild(libraryCard(item));
    }
    library.shown += page.length;
    elements.libraryMore.hidden = library.shown >= items.length;
    updateLibraryHint(items.length);
  }

  function updateLibraryHint(count) {
    const library = state.library;
    const noun = library.type === 'quran' ? (count === 1 ? 'verse' : 'verses') : 'hadith';
    if (count === 0) {
      elements.libraryHint.textContent = library.hidden
        ? `No removed ${library.type === 'quran' ? 'verses' : 'hadith'}.`
        : 'Nothing here yet.';
      return;
    }
    elements.libraryHint.textContent = library.hidden
      ? `${count} removed ${noun}. Restore any to show it again.`
      : `${count} ${noun} in rotation, most recently shown first. Remove any you don't want shown.`;
  }

  function libraryCard(item) {
    const card = document.createElement('article');
    card.className = 'library-card';

    const top = document.createElement('div');
    top.className = 'library-card-top';
    const ref = document.createElement('span');
    ref.className = 'library-ref';
    ref.textContent = item.grade ? `${item.ref} · ${item.grade}` : item.ref;
    const meta = document.createElement('span');
    meta.className = 'library-meta';
    meta.textContent = shownLabel(item.lastShownAt);
    const copy = document.createElement('button');
    copy.type = 'button';
    copy.className = 'icon-button library-copy';
    const reference = window.ContentRef.format(item);
    copy.setAttribute('aria-label', `Copy reference ${reference}`);
    copy.title = `Copy ${reference}`;
    copy.innerHTML = COPY_ICON;
    copy.addEventListener('click', () => copyReference(reference, copy));
    top.append(ref, meta, copy);

    const arabic = document.createElement('p');
    arabic.className = 'library-arabic';
    arabic.lang = 'ar';
    arabic.dir = 'rtl';
    arabic.textContent = item.arabic;

    const english = document.createElement('p');
    english.className = 'library-english';
    english.textContent = item.english;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = item.hidden ? 'secondary-button' : 'ghost-button library-remove';
    button.textContent = item.hidden ? 'Restore to rotation' : 'Remove from rotation';
    button.addEventListener('click', () => toggleHidden(item, card, button));

    card.append(top, arabic, english, button);
    return card;
  }

  const COPY_ICON = '<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">' +
    '<path fill="currentColor" d="M16 1H6a2 2 0 0 0-2 2v12h2V3h10V1Zm3 4H10a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2Zm0 16h-9V7h9v14Z"/></svg>';
  const CHECK_ICON = '<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">' +
    '<path fill="currentColor" d="M9 16.2 4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2Z"/></svg>';

  /** Copies only the reference, e.g. "Quran (9:10)" or "Hadis (Bukhari: 2/1)". */
  async function copyReference(text, button) {
    try {
      if (isNativeHost) {
        // The app's clipboard is more reliable than the WebView's.
        const bridge = await loadNativeBridge();
        await bridge.InvokeDotNet('CopyText', [text]);
      } else {
        let copied = false;
        if (navigator.clipboard && window.isSecureContext) {
          try {
            await navigator.clipboard.writeText(text);
            copied = true;
          } catch {
            // Refused (no permission or focus): try the older way below.
          }
        }
        if (!copied) {
          const area = document.createElement('textarea');
          area.value = text;
          area.setAttribute('readonly', '');
          area.style.position = 'fixed';
          area.style.opacity = '0';
          elements.libraryDialog.append(area);
          area.select();
          copied = document.execCommand('copy');
          area.remove();
        }
        if (!copied) {
          throw new Error('Copy is not available here.');
        }
      }
      button.innerHTML = CHECK_ICON;
      button.classList.add('is-done');
      elements.libraryHint.textContent = `Copied: ${text}`;
      window.setTimeout(() => {
        button.innerHTML = COPY_ICON;
        button.classList.remove('is-done');
      }, 1500);
    } catch (error) {
      console.warn('Copy failed.', error);
      elements.libraryHint.textContent = `Could not copy. The reference is ${text}.`;
    }
  }

  function shownLabel(ms) {
    if (typeof ms !== 'number') {
      return 'Not shown yet';
    }
    const timeZone = state.data?.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone;
    const date = new Date(ms);
    const clock = formatClockTime(date, timeZone);
    const today = getDateKey(new Date(nowMs()), timeZone);
    if (getDateKey(date, timeZone) === today) {
      return `Shown today ${clock.time} ${clock.meridiem}`;
    }
    const day = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', timeZone }).format(date);
    return `Shown ${day}, ${clock.time} ${clock.meridiem}`;
  }

  async function toggleHidden(item, card, button) {
    button.disabled = true;
    try {
      const updated = await request(`/api/content/${item.id}/hidden`, {
        method: 'PUT',
        body: JSON.stringify({ hidden: !item.hidden })
      });
      state.library.items = state.library.items.filter((other) => other.id !== item.id);
      state.library.shown = Math.max(0, state.library.shown - 1);
      card.remove();
      updateLibraryHint(filteredLibrary().length);
      // Toasts sit behind the modal dialog, so confirm inside the Library.
      elements.libraryHint.textContent = `${updated && updated.hidden ? 'Removed from rotation' : 'Restored to rotation'}: ${item.ref}. ${elements.libraryHint.textContent}`;
      if (state.contentPick && state.contentPick.item && state.contentPick.item.id === item.id) {
        state.contentPick = null;
      }
      ensureActiveContent(true);
    } catch (error) {
      button.disabled = false;
      elements.libraryHint.textContent = 'Could not update. Try again.';
      console.warn('Hide/restore failed.', error);
    }
  }

  // ---------------------------------------------------------------------------------------------
  // Weekly content update status (Settings)
  // ---------------------------------------------------------------------------------------------

  async function loadContentStatus() {
    window.clearTimeout(state.contentStatusTimer);
    try {
      const status = await request('/api/content/status');
      renderContentStatus(status);
      if (status.refreshing && elements.settingsDialog.open) {
        state.contentStatusTimer = window.setTimeout(loadContentStatus, 3000);
      } else if (state.contentRefreshRequested) {
        state.contentRefreshRequested = false;
        ensureActiveContent(true);
      }
    } catch (error) {
      elements.contentStatus.textContent = 'Content update status is unavailable.';
      console.warn('Content status failed.', error);
    }
  }

  function formatWhen(ms) {
    const timeZone = state.data?.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone;
    return new Intl.DateTimeFormat('en-GB', {
      day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit', hour12: true, timeZone
    }).format(new Date(ms));
  }

  function renderContentStatus(status) {
    const lines = [];
    let rotation = `${status.quranCount} ayat and ${status.hadithCount} hadith in rotation`;
    if (status.hiddenCount) {
      rotation += `, ${status.hiddenCount} removed`;
    }
    lines.push(`${rotation}.`);

    if (status.refreshing) {
      lines.push('Downloading new Ayat and Hadith…');
    } else {
      if (status.lastRefreshAt) {
        lines.push(`Last update ${formatWhen(status.lastRefreshAt)}: ${status.lastResult || ''}`.trim());
      } else {
        lines.push('No weekly update yet.');
      }
      if (status.lastAttemptAt && (!status.lastRefreshAt || status.lastAttemptAt > status.lastRefreshAt) && status.lastResult) {
        lines.push(status.lastResult);
      }
      const next = Math.max(status.nextDueAt, Date.now());
      lines.push(`Next update ${next <= Date.now() + MINUTE ? 'when online' : formatWhen(next)}${isNativeHost ? ', on Wi-Fi' : ''}.`);
    }

    elements.contentStatus.textContent = lines.join(' ');
    elements.contentRefreshButton.disabled = Boolean(status.refreshing);
  }

  async function startContentRefresh() {
    elements.contentRefreshButton.disabled = true;
    state.contentRefreshRequested = true;
    try {
      await request('/api/content/refresh', { method: 'POST' });
      elements.contentStatus.textContent = 'Downloading new Ayat and Hadith…';
    } catch (error) {
      showToast('Could not start the update.');
      console.warn('Refresh start failed.', error);
    }
    state.contentStatusTimer = window.setTimeout(loadContentStatus, 1500);
  }
})();
