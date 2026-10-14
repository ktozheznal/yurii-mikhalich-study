const sessionState = document.querySelector("#session-state");
const profile = document.querySelector("#profile");
const createFields = document.querySelector("#create-fields");
const logoutButton = document.querySelector("#logout");
const createResult = document.querySelector("#create-result");

function clearSession(message) {
  profile.textContent = "";
  profile.hidden = true;
  createFields.disabled = true;
  logoutButton.disabled = true;
  sessionState.textContent = message;
  createResult.textContent = "";
}

async function request(path, body) {
  const response = await fetch(path, {
    method: body === undefined ? "GET" : "POST",
    credentials: "same-origin",
    headers: { Accept: "application/json", ...(body === undefined ? {} : { "Content-Type": "application/json" }) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (!response.ok) {
    if (response.status === 401) clearSession("Анонімний сеанс · HTTP 401. Увійдіть до облікового запису.");
    const problem = await response.json().catch(() => null);
    const message = response.status === 429
      ? `Забагато спроб входу. Повторіть через ${response.headers.get("Retry-After") ?? "60"} с.`
      : response.status === 401 ? "Потрібен вхід або введені невірні облікові дані."
      : problem?.title ?? "Запит відхилено.";
    const error = new Error(`HTTP ${response.status}: ${message}`);
    error.status = response.status;
    throw error;
  }
  if (response.status === 204) return null;
  if (!response.headers.get("Content-Type")?.includes("application/json"))
    throw new Error("Неочікуваний формат відповіді.");
  return response.json();
}

async function refreshProfile() {
  // Clear stale identity before a request, including the network-error case.
  clearSession("Перевірка сеансу…");
  try {
    const me = await request("/api/me");
    profile.textContent = `Користувач: ${me.userName}\nID: ${me.userId}\nРоль: ${me.role}`;
    profile.hidden = false;
    createFields.disabled = false;
    logoutButton.disabled = false;
    sessionState.textContent = "Активний сеанс · HTTP 200";
    return true;
  } catch (error) {
    if (error.status !== 401) clearSession("Помилка перевірки сеансу. Спробуйте ще раз.");
    return false;
  }
}

document.querySelector("#login").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const button = form.querySelector("button");
  const result = document.querySelector("#login-result");
  const values = new FormData(form);
  button.disabled = true;
  result.textContent = "Вхід…";
  try {
    await request("/api/auth/login", { userName: values.get("userName"), password: values.get("password") });
    result.textContent = "Вхід виконано · HTTP 204";
    await refreshProfile();
  } catch (error) {
    result.textContent = error.status ? error.message : "Помилка мережі. Спробуйте ще раз.";
  } finally {
    form.elements.password.value = "";
    button.disabled = false;
  }
});

document.querySelector("#register").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const values = new FormData(form);
  const button = form.querySelector("button");
  const result = document.querySelector("#register-result");
  button.disabled = true;
  try {
    await request("/api/auth/register", {
      userName: values.get("userName"), email: values.get("email"),
      displayName: values.get("displayName"), password: values.get("password"),
    });
    document.querySelector("#login").elements.userName.value = values.get("userName");
    result.textContent = "Обліковий запис створено · HTTP 201. Тепер увійдіть.";
  } catch (error) {
    result.textContent = error.status ? error.message : "Помилка мережі. Спробуйте ще раз.";
  } finally {
    form.elements.password.value = "";
    button.disabled = false;
  }
});

document.querySelector("#create").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = event.currentTarget;
  const values = new FormData(form);
  const button = form.querySelector("button");
  button.disabled = true;
  try {
    const created = await request("/api/incidents", {
      title: values.get("title"), description: values.get("description"), severity: values.get("severity"),
      occurredAtUtc: new Date(values.get("occurredAtUtc")).toISOString(),
    });
    createResult.textContent = `Створено · HTTP 201: ${created.title} (ID: ${created.id})`;
    form.reset();
    setDefaultDate();
  } catch (error) {
    createResult.textContent = error.status ? error.message : "Помилка створення. Перевірте дані або мережу.";
  } finally { button.disabled = false; }
});

document.querySelector("#me").addEventListener("click", refreshProfile);
logoutButton.addEventListener("click", async () => {
  logoutButton.disabled = true;
  try {
    await request("/api/auth/logout", {});
    clearSession("Вихід виконано · HTTP 204. Перевірка доступу…");
    document.querySelector("#login-result").textContent = "";
    document.querySelector("#register-result").textContent = "";
    document.querySelector("#create").reset();
    setDefaultDate();
    await refreshProfile();
  } catch (error) {
    clearSession(error.status ? error.message : "Помилка виходу. Перевірте сеанс ще раз.");
  }
});

function setDefaultDate() {
  const now = new Date();
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60000);
  document.querySelector("#create").elements.occurredAtUtc.value = local.toISOString().slice(0, 16);
}

setDefaultDate();
refreshProfile();
