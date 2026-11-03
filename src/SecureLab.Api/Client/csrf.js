// Tokens belong to the current identity and this page only. Never persist or log them.
window.SecureLabCsrf = (() => {
  let token;
  let generation = 0;
  let pending;
  const safeMethods = new Set(["GET", "HEAD", "OPTIONS"]);
  const publicRoutes = new Set(["/api/auth/login", "/api/auth/register"]);

  function clear() {
    generation++;
    token = undefined;
    pending = undefined;
  }

  async function ensureToken() {
    if (token) return token;
    if (!pending) {
      const currentGeneration = generation;
      pending = (async () => {
        const response = await fetch("/api/antiforgery/token", {
          credentials: "same-origin", headers: { Accept: "application/json" }, cache: "no-store",
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}: Не вдалося перевірити сеанс.`);
        const body = await response.json();
        if (generation !== currentGeneration || !body.token)
          throw new Error("Сеанс змінився. Повторіть дію після перевірки профілю.");
        token = body.token;
        return token;
      })().finally(() => { if (generation === currentGeneration) pending = undefined; });
    }
    return pending;
  }

  async function send(path, options = {}) {
    const url = new URL(path, location.href);
    if (url.origin !== location.origin) throw new Error("Дозволено лише запити до поточного сайту.");
    const method = (options.method ?? "GET").toUpperCase();
    const headers = new Headers(options.headers);
    const publicMutation = publicRoutes.has(url.pathname);
    if (!safeMethods.has(method) && !publicMutation)
      headers.set("X-CSRF-TOKEN", await ensureToken());
    const response = await fetch(url, { ...options, method, headers, credentials: "same-origin" });
    if (response.status === 401 || response.ok &&
        (url.pathname === "/api/auth/login" || url.pathname === "/api/auth/logout")) clear();
    // A failed mutation is returned to the caller; it is never retried automatically.
    return response;
  }
  return Object.freeze({ fetch: send, clear });
})();
