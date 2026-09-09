# Карта архітектури

Карта складена під час ЛР 1 на baseline 2-A «Трекер інцидентів». Мета — щоб інша
людина знайшла змінений маршрут без читання всього коду.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `src/SecureLab.Api/Presentation/` | Описує endpoints, перевіряє зовнішні параметри, формує HTTP-відповідь |
| Application | `src/SecureLab.Api/Application/` | Виконує сценарій читання: список, деталі, агрегат за критичністю |
| Data (EF Core / Npgsql) | `src/SecureLab.Api/Data/` | Відображає C#-сутності на PostgreSQL, застосовує migration і seed |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері `postgres:18.4-alpine3.24` |

Компоненти запускаються й відмовляють незалежно: здоровий контейнер ще не доводить,
що API зібрано, а успішний старт API ще не доводить доступності БД. Тому `/health`
перевіряє саме зʼєднання API з PostgreSQL.

## Змінений наскрізний маршрут

Реалізована в ЛР 1 точка розширення: **`GET /api/incidents/severity-summary`**
(у baseline повертала `501 Not Implemented`).

```text
кнопка #summary-button у Client/index.html
  → loadSeveritySummary() у Client/app.js
  → GET /api/incidents/severity-summary[?status=<IncidentStatus>]
  → IncidentEndpoints.GetSeveritySummaryAsync
      (Presentation/Endpoints/IncidentEndpoints.cs)
  → IncidentQueries.GetSeveritySummaryAsync
      (Application/Incidents/IncidentQueries.cs)
  → SecureLabDbContext.Incidents   [AsNoTracking → GroupBy → Count → ToListAsync]
  → PostgreSQL: таблиця incidents
  → IncidentSeveritySummaryResponse
      (Presentation/Contracts/IncidentResponses.cs)
  → JSON response
  → textContent у #summary-list (Client/app.js)
```

### Ключові файли за рівнями

| Рівень | Файл | Символ |
|---|---|---|
| Client (розмітка) | `Client/index.html` | `#summary-button`, `#summary-status`, `#summary-list` |
| Client (логіка) | `Client/app.js` | `loadSeveritySummary()`, `apiFetch()` |
| Endpoint | `Presentation/Endpoints/IncidentEndpoints.cs` | `MapGet("/severity-summary", …)`, `GetSeveritySummaryAsync` |
| Contract | `Presentation/Contracts/IncidentResponses.cs` | `IncidentSeveritySummaryResponse` |
| Application | `Application/Incidents/IncidentQueries.cs` | `GetSeveritySummaryAsync` |
| DbContext | `Data/SecureLabDbContext.cs` | `DbSet<Incident> Incidents`, `ToTable("incidents")` |
| Таблиця | PostgreSQL | `incidents` |

Однойменні `IncidentEndpoints.GetSeveritySummaryAsync` і
`IncidentQueries.GetSeveritySummaryAsync` належать до різних типів: перший
перетворює результат на HTTP-відповідь `200` або `400`, другий читає дані й повертає DTO.

### Готовий маршрут для порівняння

`GET /api/incidents/{id:guid}` проходить ті самі рівні, але має інший контракт:
маршрутне обмеження `:guid` відсіює несумісний path parameter, а `null` із query
дає `404` Problem Details замість `400`. Некоректний параметр і відсутній ресурс —
різна семантика.

## Межі довіри

| Межа | Дані, що її перетинають | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|---|
| Користувач → Browser client | текст і вибір у формі | користувач контролює введення повністю | клієнт не є контролем; значення все одно перевіряє сервер |
| Browser client → API | `status` у query string, `id` у path | клієнт і HTTP-запит можна відтворити поза UI будь-яким HTTP-клієнтом | `Enum.TryParse` + `Enum.IsDefined` у `GetSeveritySummaryAsync` і `GetListAsync` → `400`; маршрутне обмеження `:guid` для details |
| API → PostgreSQL | `id`, умови фільтра та групування | збережений текст не стає безпечним від того, що вже в системі | параметризація EF Core, `AsNoTracking()`, явна проєкція лише в `severity`/`count` |
| API → Browser client | JSON із полями response DTO | право прочитати entity не означає право одержати всі його поля | окремий `IncidentSeveritySummaryResponse`; `IncidentDetailsResponse` без `OwnerUserId`, email і внутрішніх коментарів |
| Дані response → DOM | текстові значення з JSON | текст може бути інтерпретовано як HTML | `document.createElement` + `textContent` / `createTextNode`; автотест `ClientScript_DoesNotUseDangerousInnerHtmlSink` не допускає небезпечного sink |
| Конфігурація → API | connection string, environment variables | локальна конфігурація не придатна для іншого середовища | розділення джерел конфігурації; реальні значення передаються поза Git |

Перевірено практично: seed-інцидент «Перевірка журналу компʼютерного класу» містить
у `description` текст `<script>`, і клієнт показує його як звичайний текст.

## Конфігураційні входи

| Вхід | Що визначає | Примітка |
|---|---|---|
| `global.json` | смуга .NET SDK `10.0.3xx`, `rollForward: latestPatch` | діє у своєму каталозі та вкладених; `dotnet --version` з іншого місця не є перевіркою starter |
| `src/SecureLab.Api/appsettings.json` | базові налаштування застосунку | — |
| `src/SecureLab.Api/appsettings.Development.json` | режим міграцій і локальний connection string стенда | лише для `127.0.0.1`; не приклад для іншого середовища |
| `src/SecureLab.Api/Properties/launchSettings.json` | `applicationUrl` профілю запуску | `dotnet run` читає саме звідси, а не лише з `ASPNETCORE_URLS` |
| `infra/compose.yaml` | версія PostgreSQL, публікація порту, healthcheck | порт береться з `POSTGRES_PORT` |
| `infra/.env.example` | приклад локальної конфігурації (`POSTGRES_PORT`) | зберігається в Git; `infra/.env` — ні (перевірено `git check-ignore -v infra/.env`) |
| `ConnectionStrings__SecureLab` | перевизначення connection string | спосіб передати значення поза репозиторієм; у документацію та звіт реальні значення не потрапляють |

Залежність між входами: порт PostgreSQL задається у **двох** місцях одночасно —
у compose (через `POSTGRES_PORT`) і в connection string API. Зміна лише одного з них
дає `connection refused`, а не помилку конфігурації.

## Повернення до відомого seed-стану

```bash
# 1. зупинити API (Ctrl+C у його терміналі)
# 2. застосувати migration, очистити навчальні таблиці, повторно заповнити seed
dotnet run --project src/SecureLab.Api -- --reset-database
# 3. запустити API знову і повторити базові сценарії
dotnet run --project src/SecureLab.Api
```

Команда працює лише в явно налаштованому Development environment, виконується
разово й не запускає API після завершення. Прапорець `--no-build` можна додати,
якщо код не змінювався після останньої збірки.

Перевірено: після reset `GET /api/incidents?status=Triaged` повертає той самий
один інцидент, `?status=Resolved` — `[]`, а `severity-summary` — `Low: 1, Medium: 1,
High: 1, Critical: 0`. Стенд відтворюється без ручного редагування рядків у PostgreSQL.
