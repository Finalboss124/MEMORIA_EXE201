(function () {
  const TOKEN_KEY = "memoria.auth.token";
  const USER_KEY = "memoria.auth.user";
  const API_BASE_KEY = "memoria.api.baseUrl";
  const GOOGLE_CLIENT_ID_KEY = "memoria.google.clientId";
  const DEFAULT_API_BASE = "http://localhost:5284";
  const DEFAULT_GOOGLE_CLIENT_ID = "";

  const appConfig = window.MEMORIA_CONFIG || {};
  const apiBase = localStorage.getItem(API_BASE_KEY) || appConfig.apiBaseUrl || DEFAULT_API_BASE;
  const googleClientId = localStorage.getItem(GOOGLE_CLIENT_ID_KEY) || appConfig.googleClientId || DEFAULT_GOOGLE_CLIENT_ID;

  function getLoginUrl() {
    return new URL("memoria_login/code.html", new URL("../", window.location.href)).href;
  }

  function getDashboardUrl() {
    return new URL("memoria_dashboard/code.html", new URL("../", window.location.href)).href;
  }

  function isLoginPage() {
    return window.location.pathname.replace(/\\/g, "/").includes("/memoria_login/");
  }

  function getToken() {
    return localStorage.getItem(TOKEN_KEY);
  }

  function logout() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    window.location.href = getLoginUrl();
  }

  function requireAuth() {
    if (!isLoginPage() && !getToken()) {
      window.location.href = getLoginUrl();
    }
  }

  function initLogoutButtons() {
    document.querySelectorAll("[data-auth-logout]").forEach((button) => {
      button.addEventListener("click", (event) => {
        event.preventDefault();
        logout();
      });
    });
  }

  async function readPayload(response) {
    return response.json().catch(() => ({}));
  }

  async function login(email, password) {
    const response = await fetch(`${apiBase}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
    });

    const payload = await readPayload(response);
    if (!response.ok) {
      throw new Error(payload.message || "Unable to sign in. Please try again.");
    }

    return payload;
  }

  async function register(fullName, email, password, phoneNumber) {
    const response = await fetch(`${apiBase}/api/auth/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fullName, email, password, phoneNumber }),
    });

    const payload = await readPayload(response);
    if (!response.ok) {
      throw new Error(payload.message || "Unable to create your account. Please try again.");
    }

    return payload;
  }

  async function loginWithGoogle(idToken) {
    const response = await fetch(`${apiBase}/api/auth/google`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ idToken }),
    });

    const payload = await readPayload(response);
    if (!response.ok) {
      throw new Error(payload.message || "Unable to sign in with Google. Please try again.");
    }

    return payload;
  }

  async function verifyCode(verificationId, code) {
    const response = await fetch(`${apiBase}/api/auth/verify-login-code`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ verificationId, code }),
    });

    const payload = await readPayload(response);
    if (!response.ok) {
      throw new Error(payload.message || "The verification code is invalid.");
    }

    localStorage.setItem(TOKEN_KEY, payload.token);
    localStorage.setItem(USER_KEY, JSON.stringify(payload.user));
    return payload;
  }

  window.MemoriaAuth = {
    apiBase,
    googleClientId,
    getToken,
    login,
    register,
    loginWithGoogle,
    verifyCode,
    logout,
    requireAuth,
    getDashboardUrl,
    getLoginUrl,
  };

  document.addEventListener("DOMContentLoaded", () => {
    requireAuth();
    initLogoutButtons();
  });
})();
