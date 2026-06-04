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

  function getAdminUrl() {
    return new URL("admin_claims/code.html", new URL("../", window.location.href)).href;
  }

  function getHomeUrl() {
    return isAdmin() ? getAdminUrl() : getDashboardUrl();
  }

  function isLoginPage() {
    return window.location.pathname.replace(/\\/g, "/").includes("/memoria_login/");
  }

  function isAdminPage() {
    return window.location.pathname.replace(/\\/g, "/").includes("/admin_claims/");
  }

  function isPublicPage() {
    const path = window.location.pathname.replace(/\\/g, "/");
    return path.includes("/memoria_login/") || path.includes("/claim_legacy/");
  }

  function getToken() {
    return localStorage.getItem(TOKEN_KEY);
  }

  function getUser() {
    const raw = localStorage.getItem(USER_KEY);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  }

  function isTokenExpired(token) {
    if (!token) {
      return true;
    }

    const parts = token.split(".");
    if (parts.length !== 3) {
      return false;
    }

    try {
      const base64 = parts[1]
        .replace(/-/g, "+")
        .replace(/_/g, "/")
        .padEnd(Math.ceil(parts[1].length / 4) * 4, "=");
      const payload = JSON.parse(atob(base64));
      return typeof payload.exp !== "number" || payload.exp * 1000 <= Date.now();
    } catch {
      return false;
    }
  }

  function decodeTokenPayload(token) {
    if (!token) {
      return {};
    }

    const parts = token.split(".");
    if (parts.length !== 3) {
      return {};
    }

    try {
      const base64 = parts[1]
        .replace(/-/g, "+")
        .replace(/_/g, "/")
        .padEnd(Math.ceil(parts[1].length / 4) * 4, "=");
      return JSON.parse(atob(base64));
    } catch {
      return {};
    }
  }

  function getRoles() {
    const userRoles = getUser()?.roles || getUser()?.Roles || [];
    const payload = decodeTokenPayload(getToken());
    const tokenRoles = payload.role ||
      payload.roles ||
      payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] ||
      [];
    return []
      .concat(userRoles, tokenRoles)
      .filter(Boolean)
      .map((role) => String(role).toLowerCase());
  }

  function isAdmin() {
    return getRoles().includes("admin");
  }

  function logout() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    window.location.href = getLoginUrl();
  }

  function clearSession() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
  }

  function handleUnauthorized() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USER_KEY);
    const loginUrl = new URL(getLoginUrl());
    loginUrl.searchParams.set("reason", "session-expired");
    window.location.href = loginUrl.href;
  }

  function requireAuth() {
    const token = getToken();
    if (!isPublicPage() && (!token || isTokenExpired(token))) {
      clearSession();
      window.location.replace(getLoginUrl());
      return;
    }

  }

  async function refreshSessionUser() {
    const token = getToken();
    if (!token || isTokenExpired(token)) {
      return null;
    }

    try {
      const response = await fetch(`${apiBase}/api/auth/me`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!response.ok) {
        return null;
      }

      const profile = await response.json();
      const current = getUser() || {};
      const nextUser = { ...current, ...profile };
      localStorage.setItem(USER_KEY, JSON.stringify(nextUser));
      return nextUser;
    } catch {
      return null;
    }
  }

  async function routeAuthenticatedUser() {
    const token = getToken();
    if (!token || isTokenExpired(token)) {
      return;
    }

    await refreshSessionUser();
    if (isLoginPage()) {
      window.location.replace(getHomeUrl());
      return;
    }

    if (isAdmin() && !isAdminPage() && !isPublicPage()) {
      window.location.replace(getAdminUrl());
      return;
    }

    if (!isAdmin() && isAdminPage()) {
      window.location.replace(getDashboardUrl());
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

  function getUserKey(suffix) {
    const user = getUser();
    return `memoria.${user?.userId || "anonymous"}.${suffix}`;
  }

  function initHeaderUserMenu() {
    const header = document.querySelector("header");
    if (!header) {
      return;
    }

    header.querySelectorAll("[data-auth-logout]").forEach((button) => {
      button.classList.add("hidden");
      button.setAttribute("aria-hidden", "true");
    });

    const avatar = header.querySelector("img[alt*='profile' i], img[data-alt*='profile' i]");
    if (!avatar || avatar.dataset.memoriaMenuReady) {
      return;
    }

    avatar.dataset.memoriaMenuReady = "true";
    avatar.classList.add("cursor-pointer");
    avatar.tabIndex = 0;
    const cachedUser = getUser() || {};
    if (cachedUser.avatarUrl) {
      avatar.src = cachedUser.avatarUrl;
    }

    const menu = document.createElement("div");
    menu.className = "hidden fixed z-[80] w-44 rounded-2xl bg-surface-container-lowest p-2 shadow-xl ring-1 ring-outline-variant/40";
    menu.innerHTML = `
      <button class="flex w-full items-center gap-3 rounded-xl px-3 py-2 text-left text-sm font-semibold text-on-surface hover:bg-surface-container-high" type="button" data-profile-action>
        <span class="material-symbols-outlined text-[18px]">person</span>
        Profile
      </button>
      <button class="flex w-full items-center gap-3 rounded-xl px-3 py-2 text-left text-sm font-semibold text-on-surface hover:bg-surface-container-high" type="button" data-auth-logout-menu>
        <span class="material-symbols-outlined text-[18px]">logout</span>
        Logout
      </button>
    `;
    document.documentElement.appendChild(menu);

    const profilePanel = document.createElement("div");
    profilePanel.className = "hidden bg-[#30312c]/45 backdrop-blur-sm";
    profilePanel.innerHTML = `
      <div class="bg-surface-container-lowest p-6 shadow-xl ring-1 ring-outline-variant/40" data-profile-card style="position: fixed; left: 50vw; top: 18px; width: min(92vw, 40rem); max-height: calc(100dvh - 74px); overflow-y: auto; transform: translateX(-50%);">
        <div class="mb-5 flex items-start justify-between gap-4">
          <div class="flex items-center gap-4">
            <img alt="User avatar" class="h-20 w-20 rounded-2xl object-cover ring-2 ring-primary-container" data-profile-avatar />
            <div>
              <p class="text-sm font-bold uppercase tracking-[0.14em] text-secondary">Profile</p>
              <h2 class="mt-2 text-2xl font-bold text-on-surface" data-profile-name>User profile</h2>
              <p class="mt-1 text-sm text-on-surface-variant" data-profile-email></p>
            </div>
          </div>
          <button class="flex h-10 w-10 items-center justify-center rounded-full bg-surface-container-high text-on-surface-variant hover:bg-surface-variant" type="button" data-profile-close>
            <span class="material-symbols-outlined">close</span>
          </button>
        </div>
        <div class="grid grid-cols-1 gap-3 sm:grid-cols-2" data-profile-details></div>
        <form class="mt-4 hidden grid grid-cols-1 gap-3 sm:grid-cols-2" data-profile-form>
          <label class="text-sm font-semibold text-on-surface-variant">Full name
            <input class="mt-1 h-11 w-full rounded-xl border-0 bg-surface px-3 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="fullName" required />
          </label>
          <label class="text-sm font-semibold text-on-surface-variant">Phone
            <input class="mt-1 h-11 w-full rounded-xl border-0 bg-surface px-3 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="phoneNumber" />
          </label>
          <label class="sm:col-span-2 text-sm font-semibold text-on-surface-variant">Avatar image
            <input class="mt-1 block w-full rounded-xl bg-surface px-3 py-3 text-sm text-on-surface ring-1 ring-surface-variant file:mr-4 file:rounded-full file:border-0 file:bg-primary file:px-4 file:py-2 file:text-sm file:font-bold file:text-on-primary hover:file:opacity-90" name="avatarFile" type="file" accept="image/*" />
            <input name="avatarUrl" type="hidden" />
          </label>
          <label class="text-sm font-semibold text-on-surface-variant">Date of birth
            <input class="mt-1 h-11 w-full rounded-xl border-0 bg-surface px-3 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="dateOfBirth" type="date" />
          </label>
          <label class="text-sm font-semibold text-on-surface-variant">Gender
            <select class="mt-1 h-11 w-full rounded-xl border-0 bg-surface px-3 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="gender">
              <option value="">Not set</option>
              <option value="Female">Female</option>
              <option value="Male">Male</option>
              <option value="Other">Other</option>
            </select>
          </label>
          <label class="sm:col-span-2 text-sm font-semibold text-on-surface-variant">Address
            <textarea class="mt-1 min-h-20 w-full rounded-xl border-0 bg-surface px-3 py-2 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="address"></textarea>
          </label>
          <label class="text-sm font-semibold text-on-surface-variant">CCCD / Passport
            <input class="mt-1 h-11 w-full rounded-xl border-0 bg-surface px-3 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="cccdNumber" />
          </label>
          <label class="text-sm font-semibold text-on-surface-variant">Issue date
            <input class="mt-1 h-11 w-full rounded-xl border-0 bg-surface px-3 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="cccdIssuedDate" type="date" />
          </label>
          <label class="sm:col-span-2 text-sm font-semibold text-on-surface-variant">Issue place
            <input class="mt-1 h-11 w-full rounded-xl border-0 bg-surface px-3 text-sm text-on-surface ring-1 ring-surface-variant focus:ring-2 focus:ring-primary-container" name="cccdIssuedPlace" />
          </label>
          <p class="hidden rounded-xl bg-error-container px-3 py-2 text-sm font-semibold text-error sm:col-span-2" data-profile-error></p>
        </form>
        <div class="mt-4 flex items-center justify-end gap-3">
          <button class="rounded-full px-4 py-2 text-sm font-bold text-on-surface-variant hover:bg-surface-container-high" type="button" data-profile-cancel>Cancel</button>
          <button class="rounded-full bg-primary px-5 py-2 text-sm font-bold text-on-primary hover:opacity-90" type="button" data-profile-edit>Edit profile</button>
          <button class="hidden rounded-full bg-primary px-5 py-2 text-sm font-bold text-on-primary hover:opacity-90" type="button" data-profile-save>Save changes</button>
        </div>
      </div>
    `;
    document.documentElement.appendChild(profilePanel);

    function positionMenu() {
      const rect = avatar.getBoundingClientRect();
      menu.style.top = `${rect.bottom + 10}px`;
      menu.style.left = `${Math.max(12, rect.right - 176)}px`;
    }

    function toggleMenu() {
      positionMenu();
      menu.classList.toggle("hidden");
    }

    async function fetchProfile() {
      const token = getToken();
      if (!token) {
        return getUser() || {};
      }

      const response = await fetch(`${apiBase}/api/auth/me`, {
        headers: {
          Authorization: `Bearer ${token}`,
          "X-Memoria-User-Id": getUser()?.userId || "",
        },
      });
      if (!response.ok) {
        return getUser() || {};
      }

      const profile = await response.json();
      const current = getUser() || {};
      localStorage.setItem(USER_KEY, JSON.stringify({ ...current, ...profile }));
      return profile;
    }

    function getFallbackAvatar(profile) {
      const label = (profile.fullName || profile.email || "MU")
        .split(/\s+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((part) => part[0])
        .join("")
        .toUpperCase();
      const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="160" height="160" viewBox="0 0 160 160"><rect width="160" height="160" rx="32" fill="#fadadd"/><text x="80" y="92" text-anchor="middle" font-family="Arial, sans-serif" font-size="52" font-weight="700" fill="#70585b">${escapeHtml(label || "MU")}</text></svg>`;
      return `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(svg)}`;
    }

    function formatProfileDate(value) {
      if (!value) {
        return "Not set";
      }

      const date = new Date(String(value).endsWith("Z") ? value : `${value}Z`);
      if (Number.isNaN(date.getTime())) {
        return String(value);
      }

      return date.toLocaleString();
    }

    function renderProfile(profile) {
      const avatarUrl = profile.avatarUrl || getFallbackAvatar(profile);
      profilePanel.querySelector("[data-profile-avatar]").src = avatarUrl;
      profilePanel.querySelector("[data-profile-avatar]").onerror = () => {
        profilePanel.querySelector("[data-profile-avatar]").src = getFallbackAvatar(profile);
      };
      avatar.src = avatarUrl;
      avatar.onerror = () => {
        avatar.src = getFallbackAvatar(profile);
      };
      profilePanel.querySelector("[data-profile-name]").textContent = profile.fullName || profile.name || "Memoria user";
      profilePanel.querySelector("[data-profile-email]").textContent = profile.email || "";
      profilePanel.querySelector("[data-profile-details]").innerHTML = [
        ["Phone", profile.phoneNumber || "Not set"],
        ["Status", profile.isActive === false ? "Inactive" : "Active"],
        ["Gender", profile.gender || "Not set"],
        ["Date of birth", profile.dateOfBirth || "Not set"],
        ["Address", profile.address || "Not set"],
        ["CCCD / Passport", profile.cccdNumber || "Not set"],
        ["Issue date", profile.cccdIssuedDate || "Not set"],
        ["Issue place", profile.cccdIssuedPlace || "Not set"],
        ["Created at", formatProfileDate(profile.createdAt)],
        ["Last login", formatProfileDate(profile.lastLoginAt)],
      ].map(([label, value]) => `
        <div class="rounded-2xl bg-surface p-4">
          <p class="text-xs font-bold uppercase tracking-[0.12em] text-on-surface-variant">${escapeHtml(label)}</p>
          <p class="mt-1 break-words text-sm font-semibold text-on-surface">${escapeHtml(value)}</p>
        </div>
      `).join("");
      fillProfileForm(profile);
      setProfileEditing(false);
    }

    function fillProfileForm(profile) {
      const form = profilePanel.querySelector("[data-profile-form]");
      form.elements.fullName.value = profile.fullName || "";
      form.elements.phoneNumber.value = profile.phoneNumber || "";
      form.elements.avatarUrl.value = profile.avatarUrl || "";
      form.elements.avatarFile.value = "";
      form.elements.dateOfBirth.value = profile.dateOfBirth || "";
      form.elements.gender.value = profile.gender || "";
      form.elements.address.value = profile.address || "";
      form.elements.cccdNumber.value = profile.cccdNumber || "";
      form.elements.cccdIssuedDate.value = profile.cccdIssuedDate || "";
      form.elements.cccdIssuedPlace.value = profile.cccdIssuedPlace || "";
    }

    function previewSelectedAvatar() {
      const file = profilePanel.querySelector("[name='avatarFile']")?.files?.[0];
      if (!file || !file.type.startsWith("image/")) {
        return;
      }

      const previewUrl = URL.createObjectURL(file);
      const profileAvatar = profilePanel.querySelector("[data-profile-avatar]");
      profileAvatar.src = previewUrl;
      avatar.src = previewUrl;
    }

    function setProfileEditing(isEditing) {
      profilePanel.querySelector("[data-profile-details]").classList.toggle("hidden", isEditing);
      profilePanel.querySelector("[data-profile-form]").classList.toggle("hidden", !isEditing);
      profilePanel.querySelector("[data-profile-edit]").classList.toggle("hidden", isEditing);
      profilePanel.querySelector("[data-profile-save]").classList.toggle("hidden", !isEditing);
      profilePanel.querySelector("[data-profile-cancel]").classList.toggle("hidden", !isEditing);
      profilePanel.querySelector("[data-profile-error]").classList.add("hidden");
    }

    async function saveProfile() {
      const form = profilePanel.querySelector("[data-profile-form]");
      const error = profilePanel.querySelector("[data-profile-error]");

      try {
        let avatarUrl = form.elements.avatarUrl.value.trim() || null;
        const avatarFile = form.elements.avatarFile.files?.[0];
        if (avatarFile) {
          const avatarData = new FormData();
          avatarData.append("avatar", avatarFile);
          const avatarResponse = await fetch(`${apiBase}/api/auth/me/avatar`, {
            method: "POST",
            headers: {
              Authorization: `Bearer ${getToken()}`,
              "X-Memoria-User-Id": getUser()?.userId || "",
            },
            body: avatarData,
          });
          const avatarResult = await readResponsePayload(avatarResponse);
          if (!avatarResponse.ok) {
            throw new Error(avatarResult.message || "Unable to upload avatar.");
          }
          avatarUrl = avatarResult.avatarUrl || avatarUrl;
        }

        const payload = {
          fullName: form.elements.fullName.value.trim(),
          phoneNumber: form.elements.phoneNumber.value.trim() || null,
          avatarUrl,
          dateOfBirth: form.elements.dateOfBirth.value || null,
          gender: form.elements.gender.value || null,
          address: form.elements.address.value.trim() || null,
          cccdNumber: form.elements.cccdNumber.value.trim() || null,
          cccdIssuedDate: form.elements.cccdIssuedDate.value || null,
          cccdIssuedPlace: form.elements.cccdIssuedPlace.value.trim() || null,
        };

        const response = await fetch(`${apiBase}/api/auth/me`, {
          method: "PUT",
          headers: {
            "Content-Type": "application/json",
            Authorization: `Bearer ${getToken()}`,
            "X-Memoria-User-Id": getUser()?.userId || "",
          },
          body: JSON.stringify(payload),
        });
        const result = await readResponsePayload(response);
        if (!response.ok) {
          throw new Error(result.message || "Unable to update profile.");
        }

        const current = getUser() || {};
        localStorage.setItem(USER_KEY, JSON.stringify({ ...current, ...result }));
        renderProfile(result);
      } catch (err) {
        error.textContent = err.message;
        error.classList.remove("hidden");
      }
    }

    async function readResponsePayload(response) {
      const text = await response.text().catch(() => "");
      if (!text) {
        return {};
      }

      try {
        return JSON.parse(text);
      } catch {
        return { message: text };
      }
    }

    async function openProfile() {
      const fallback = getUser() || {};
      renderProfile(fallback);
      Object.assign(profilePanel.style, {
        position: "fixed",
        left: "0",
        top: "0",
        width: "100vw",
        height: "100vh",
        zIndex: "9999",
        display: "block",
        overflow: "hidden",
      });
      document.documentElement.style.overflow = "hidden";
      document.body.style.overflow = "hidden";
      profilePanel.classList.remove("hidden");
      menu.classList.add("hidden");

      try {
        renderProfile(await fetchProfile());
      } catch {
        renderProfile(fallback);
      }
    }

    function closeProfile() {
      profilePanel.classList.add("hidden");
      profilePanel.style.display = "none";
      document.documentElement.style.overflow = "";
      document.body.style.overflow = "";
    }

    avatar.addEventListener("click", toggleMenu);
    avatar.addEventListener("keydown", (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        toggleMenu();
      }
    });
    menu.querySelector("[data-profile-action]").addEventListener("click", openProfile);
    menu.querySelector("[data-auth-logout-menu]").addEventListener("click", logout);
    profilePanel.querySelector("[data-profile-edit]").addEventListener("click", () => setProfileEditing(true));
    profilePanel.querySelector("[data-profile-cancel]").addEventListener("click", () => setProfileEditing(false));
    profilePanel.querySelector("[data-profile-save]").addEventListener("click", saveProfile);
    profilePanel.querySelector("[name='avatarFile']").addEventListener("change", previewSelectedAvatar);
    profilePanel.querySelector("[data-profile-close]").addEventListener("click", closeProfile);
    document.addEventListener("click", (event) => {
      if (!menu.contains(event.target) && event.target !== avatar) {
        menu.classList.add("hidden");
      }
    });
  }

  function initNotifications() {
    const token = getToken();
    const user = getUser();
    const header = document.querySelector("header");
    if (!token || !user || !header) {
      return;
    }

    const bell = Array.from(header.querySelectorAll("button")).find((button) =>
      Array.from(button.querySelectorAll(".material-symbols-outlined")).some((icon) => icon.textContent.trim() === "notifications")
    );
    if (!bell || bell.dataset.memoriaNotificationsReady) {
      return;
    }

    bell.dataset.memoriaNotificationsReady = "true";
    bell.classList.add("relative");
    bell.type = "button";

    const badge = document.createElement("span");
    badge.className = "hidden absolute right-1.5 top-1.5 min-h-2 min-w-2 rounded-full bg-error px-1 text-[10px] font-bold leading-4 text-white";
    bell.appendChild(badge);

    const panel = document.createElement("div");
    panel.className = "hidden fixed z-[80] w-80 rounded-2xl bg-surface-container-lowest p-3 shadow-xl ring-1 ring-outline-variant/40";
    panel.innerHTML = `
      <div class="mb-2 flex items-center justify-between px-1">
        <p class="text-sm font-bold text-on-surface">Notifications</p>
        <button class="rounded-full px-2 py-1 text-xs font-semibold text-on-surface-variant hover:bg-surface-container-high" type="button" data-mark-read>Mark read</button>
      </div>
      <div class="max-h-80 overflow-y-auto" data-notification-list></div>
    `;
    document.body.appendChild(panel);

    let deliveredLetters = [];
    let familyInvitations = [];

    function readSeenIds() {
      try {
        return new Set(JSON.parse(localStorage.getItem(getUserKey("seenNotifications")) || "[]"));
      } catch {
        return new Set();
      }
    }

    function writeSeenIds(ids) {
      localStorage.setItem(getUserKey("seenNotifications"), JSON.stringify(Array.from(ids)));
    }

    function getNotificationId(item) {
      if (item.type === "familyInvite") {
        return `familyInvite:${item.vaultMemberId}`;
      }
      return `${item.letterId}:${item.deliveredAt || "delivered"}`;
    }

    function renderNotifications() {
      const seen = readSeenIds();
      const items = [
        ...familyInvitations.map((invite) => ({ ...invite, type: "familyInvite" })),
        ...deliveredLetters.map((letter) => ({ ...letter, type: "futureLetter" })),
      ];
      const unread = items.filter((item) => !seen.has(getNotificationId(item)));
      badge.textContent = unread.length > 9 ? "9+" : String(unread.length);
      badge.classList.toggle("hidden", unread.length === 0);

      const list = panel.querySelector("[data-notification-list]");
      if (!items.length) {
        list.innerHTML = `<div class="rounded-xl bg-surface p-4 text-sm text-on-surface-variant">No notifications yet.</div>`;
        return;
      }

      list.innerHTML = items
        .slice(0, 10)
        .map((item) => {
          const isUnread = !seen.has(getNotificationId(item));
          if (item.type === "familyInvite") {
            const when = item.invitedAt ? new Date(`${item.invitedAt}Z`).toLocaleString() : "Just now";
            return `
              <div class="mb-2 rounded-xl ${isUnread ? "bg-primary-container" : "bg-surface"} p-3">
                <div class="flex items-start gap-3">
                  <span class="material-symbols-outlined mt-0.5 text-[18px] text-primary">group_add</span>
                  <div class="min-w-0 flex-1">
                    <p class="text-sm font-bold text-on-surface">${escapeHtml(item.ownerName)} invited you to ${escapeHtml(item.vaultName)}.</p>
                    <p class="mt-1 text-xs text-on-surface-variant">${escapeHtml(when)}</p>
                    <div class="mt-3 flex gap-2">
                      <button class="rounded-full bg-primary px-3 py-1.5 text-xs font-bold text-on-primary" type="button" data-family-invite-accept="${escapeHtml(item.vaultMemberId)}">Accept</button>
                      <button class="rounded-full bg-surface-container-high px-3 py-1.5 text-xs font-bold text-on-surface-variant" type="button" data-family-invite-reject="${escapeHtml(item.vaultMemberId)}">Reject</button>
                    </div>
                  </div>
                </div>
              </div>
            `;
          }
          const when = item.deliveredAt ? new Date(`${item.deliveredAt}Z`).toLocaleString() : "Just now";
          return `
            <div class="mb-2 rounded-xl ${isUnread ? "bg-primary-container" : "bg-surface"} p-3">
              <div class="flex items-start gap-3">
                <span class="material-symbols-outlined mt-0.5 text-[18px] text-primary">mark_email_read</span>
                <div>
                  <p class="text-sm font-bold text-on-surface">Your letter "${escapeHtml(item.title)}" was sent.</p>
                  <p class="mt-1 text-xs text-on-surface-variant">${escapeHtml(when)}</p>
                </div>
              </div>
            </div>
          `;
        })
        .join("");
    }

    function positionPanel() {
      const rect = bell.getBoundingClientRect();
      panel.style.top = `${rect.bottom + 10}px`;
      panel.style.left = `${Math.max(12, rect.right - 320)}px`;
    }

    async function loadNotifications() {
      try {
        const response = await fetch(`${apiBase}/api/future-letters`, {
          headers: {
            Authorization: `Bearer ${token}`,
            "X-Memoria-User-Id": user.userId || "",
          },
        });
        if (!response.ok) {
          return;
        }

        const letters = await response.json();
        const inviteResponse = await fetch(`${apiBase}/api/family-vault/invitations`, {
          headers: {
            Authorization: `Bearer ${token}`,
            "X-Memoria-User-Id": user.userId || "",
          },
        }).catch(() => null);
        const previousUnread = deliveredLetters.length || familyInvitations.length
          ? [
              ...familyInvitations.map((invite) => ({ ...invite, type: "familyInvite" })),
              ...deliveredLetters.map((letter) => ({ ...letter, type: "futureLetter" })),
            ].filter((item) => !readSeenIds().has(getNotificationId(item))).length
          : 0;
        familyInvitations = inviteResponse?.ok ? await inviteResponse.json() : [];
        deliveredLetters = letters
          .filter((letter) => letter.sealStatus === "Delivered" || letter.deliveredAt)
          .sort((a, b) => new Date(b.deliveredAt || b.deliveryDate) - new Date(a.deliveredAt || a.deliveryDate));
        renderNotifications();

        const currentUnread = [
          ...familyInvitations.map((invite) => ({ ...invite, type: "familyInvite" })),
          ...deliveredLetters.map((letter) => ({ ...letter, type: "futureLetter" })),
        ].filter((item) => !readSeenIds().has(getNotificationId(item))).length;
        if (previousUnread === 0 && currentUnread > 0 && !panel.classList.contains("hidden")) {
          renderNotifications();
        }
      } catch {
      }
    }

    bell.addEventListener("click", async () => {
      positionPanel();
      panel.classList.toggle("hidden");
      await loadNotifications();
    });

    panel.querySelector("[data-mark-read]").addEventListener("click", () => {
      const seen = readSeenIds();
      deliveredLetters.forEach((letter) => seen.add(getNotificationId(letter)));
      familyInvitations.forEach((invite) => seen.add(getNotificationId({ ...invite, type: "familyInvite" })));
      writeSeenIds(seen);
      renderNotifications();
    });

    panel.addEventListener("click", async (event) => {
      const acceptButton = event.target.closest("[data-family-invite-accept]");
      const rejectButton = event.target.closest("[data-family-invite-reject]");
      const memberId = acceptButton?.dataset.familyInviteAccept || rejectButton?.dataset.familyInviteReject;
      if (!memberId) {
        return;
      }

      event.preventDefault();
      const response = await fetch(`${apiBase}/api/family-vault/invitations/${memberId}/respond`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
          "X-Memoria-User-Id": user.userId || "",
        },
        body: JSON.stringify({ accept: Boolean(acceptButton) }),
      });
      if (response.ok) {
        await loadNotifications();
        if (Boolean(acceptButton) && window.location.pathname.includes("/memoria_family_vault/")) {
          window.location.reload();
        }
      }
    });

    document.addEventListener("click", (event) => {
      if (!panel.contains(event.target) && !bell.contains(event.target)) {
        panel.classList.add("hidden");
      }
    });

    loadNotifications();
    window.setInterval(loadNotifications, 10000);
  }

  function escapeHtml(value) {
    return String(value ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#039;");
  }

  async function readPayload(response) {
    return response.json().catch(() => ({}));
  }

  async function login(email, password) {
    clearSession();
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
    clearSession();
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
    clearSession();
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

    storeSession(payload);
    return payload;
  }

  function storeSession(payload) {
    localStorage.setItem(TOKEN_KEY, payload.token);
    localStorage.setItem(USER_KEY, JSON.stringify(payload.user));
  }

  async function validateSession() {
    const token = getToken();
    if (!token) {
      return false;
    }

    const response = await fetch(`${apiBase}/api/auth/me`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });

    return response.ok;
  }

  window.MemoriaAuth = {
    apiBase,
    googleClientId,
    getToken,
    getUser,
    isTokenExpired,
    login,
    register,
    loginWithGoogle,
    verifyCode,
    storeSession,
    validateSession,
    refreshSessionUser,
    routeAuthenticatedUser,
    clearSession,
    logout,
    handleUnauthorized,
    requireAuth,
    getDashboardUrl,
    getAdminUrl,
    getHomeUrl,
    getLoginUrl,
    getRoles,
    isAdmin,
  };

  document.addEventListener("DOMContentLoaded", () => {
    requireAuth();
    routeAuthenticatedUser();
    initLogoutButtons();
    initHeaderUserMenu();
    initNotifications();
  });
})();
