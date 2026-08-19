// Pocket Money client interop: localStorage session/history + infinite scroll.
window.pmInterop = {
  // --- generic localStorage ---
  getItem(key) { return window.localStorage.getItem(key); },
  setItem(key, value) { window.localStorage.setItem(key, value); },
  removeItem(key) { window.localStorage.removeItem(key); },

  // --- ChildrenHistory Storage (UI Spec §3.1): { AccountID: { name, lockedUntil } }
  // lockedUntil is cached from a 423 login attempt — the UI has no other way
  // to know lock state (UI Spec §3.1 clarification). Expired locks are
  // pruned on read.
  getChildrenHistory() {
    try {
      const raw = window.localStorage.getItem('pm_children_history');
      const parsed = raw ? JSON.parse(raw) : {};
      const now = Date.now();
      const out = {};
      for (const [aid, v] of Object.entries(parsed)) {
        // tolerate legacy string values
        const entry = typeof v === 'string' ? { name: v, lockedUntil: null } : v;
        if (entry.lockedUntil && entry.lockedUntil !== 'permanent' && new Date(entry.lockedUntil).getTime() < now) {
          entry.lockedUntil = null; // timed lock expired
        }
        out[aid] = entry;
      }
      return out;
    } catch { return {}; }
  },
  upsertChildrenHistory(accountId, displayName, lockedUntil) {
    const h = this.getChildrenHistory();
    const prev = h[accountId] || {};
    h[accountId] = { name: displayName, lockedUntil: lockedUntil ?? prev.lockedUntil ?? null };
    window.localStorage.setItem('pm_children_history', JSON.stringify(h));
  },
  setChildLocked(accountId, lockedUntil) {
    const h = this.getChildrenHistory();
    if (h[accountId]) {
      h[accountId].lockedUntil = lockedUntil;
      window.localStorage.setItem('pm_children_history', JSON.stringify(h));
    }
  },
  removeChildFromHistory(accountId) {
    const h = this.getChildrenHistory();
    delete h[accountId];
    window.localStorage.setItem('pm_children_history', JSON.stringify(h));
  },
  clearChildrenHistory() { window.localStorage.removeItem('pm_children_history'); },

  // --- single active session (UI Spec §3.4) ---
  getSession() {
    try {
      const raw = window.localStorage.getItem('pm_session');
      return raw ? JSON.parse(raw) : null;
    } catch { return null; }
  },
  setSession(sessionJson) { window.localStorage.setItem('pm_session', sessionJson); },
  clearSession() { window.localStorage.removeItem('pm_session'); },

  // --- infinite scroll: watch an element, notify .NET near bottom ---
  _scrollObservers: {},
  watchScroll(dotNetRef, elementId, thresholdPx) {
    const el = document.getElementById(elementId);
    if (!el) return;
    const handler = () => {
      const rect = el.getBoundingClientRect();
      if (rect.bottom < window.innerHeight + thresholdPx) {
        dotNetRef.invokeMethodAsync('OnNearBottom');
      }
    };
    window.addEventListener('scroll', handler, { passive: true });
    this._scrollObservers[elementId] = handler;
  },
  unwatchScroll(elementId) {
    const h = this._scrollObservers[elementId];
    if (h) { window.removeEventListener('scroll', h); delete this._scrollObservers[elementId]; }
  },

  // --- inactivity tracking (parent idle-lock): forward DOM events to .NET ---
  _activityHandlers: [],
  startActivityTracking(dotNetRef) {
    const events = ['mousedown', 'keydown', 'touchstart', 'scroll', 'visibilitychange'];
    const handler = () => dotNetRef.invokeMethodAsync('OnUserActivity');
    events.forEach(e => window.addEventListener(e, handler, { passive: true }));
    this._activityHandlers.push(handler);
  },
  stopActivityTracking() {
    this._activityHandlers.forEach(h => {
      ['mousedown', 'keydown', 'touchstart', 'scroll', 'visibilitychange']
        .forEach(e => window.removeEventListener(e, h));
    });
    this._activityHandlers = [];
  },

  // focus first empty account-id input box
  focusElement(id) {
    const el = document.getElementById(id);
    if (el) el.focus();
  },

  setBodyClass(add, remove) {
    if (add) document.body.classList.add(...add.split(' ').filter(Boolean));
    if (remove) document.body.classList.remove(...remove.split(' ').filter(Boolean));
  },
};
