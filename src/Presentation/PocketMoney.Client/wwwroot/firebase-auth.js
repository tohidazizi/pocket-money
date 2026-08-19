// Firebase Auth bridge for the Blazor WASM client (parent login).
// Loads its config from firebase-config.json at runtime; exposes a small
// promise-based API consumed via IJSRuntime. Uses the Firebase COMPAT SDK
// globals (firebase-app-compat.js / firebase-auth-compat.js in index.html),
// which expose the classic `firebase` global namespace.

const pmFirebase = (() => {
  let app = null;
  let auth = null;
  let googleProvider = null;
  let loading = null;

  async function ensureInit() {
    if (auth) return;
    if (loading) { await loading; return; }
    loading = (async () => {
      if (typeof firebase === 'undefined') {
        throw new Error('Firebase SDK not loaded');
      }
      const res = await fetch('firebase-config.json');
      const config = await res.json();
      app = firebase.initializeApp(config);
      auth = firebase.auth();
      googleProvider = new firebase.auth.GoogleAuthProvider();
    })();
    try {
      await loading;
    } finally {
      loading = null;
    }
  }

  return {
    // Email/password sign-in. Resolves with { uid, displayName, email }.
    async signInWithEmail(email, password) {
      await ensureInit();
      const cred = await auth.signInWithEmailAndPassword(email, password);
      return _userShape(cred.user);
    },

    // Google popup sign-in. Resolves with { uid, displayName, email }.
    async signInWithGoogle() {
      await ensureInit();
      const cred = await auth.signInWithPopup(googleProvider);
      return _userShape(cred.user);
    },

    // Current user's fresh ID token (for API calls). null if signed out.
    async getIdToken() {
      await ensureInit();
      const user = auth.currentUser;
      if (!user) return null;
      return await user.getIdToken(true);
    },

    // Subscribe to auth-state changes; .NET callback via DotNetObjectReference.
    onAuthChanged(dotNetRef) {
      ensureInit().then(() => {
        const unsub = auth.onAuthStateChanged((user) => {
          const shape = user ? _userShape(user) : null;
          dotNetRef.invokeMethodAsync('OnAuthChanged', shape);
        });
        window.__pmFirebaseUnsub = unsub;
      });
    },

    unsubscribe() {
      if (window.__pmFirebaseUnsub) { window.__pmFirebaseUnsub(); window.__pmFirebaseUnsub = null; }
    },

    async signOut() {
      await ensureInit();
      await auth.signOut();
    },
  };

  function _userShape(user) {
    return { uid: user.uid, displayName: user.displayName, email: user.email };
  }
})();
