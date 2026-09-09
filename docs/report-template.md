# Звіт до лабораторної роботи № 1

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Робоча гілка: `lab/1-system`.
- Основна гілка: `main`.
- Фінальний тег: `TODO` (створюється на етапі подання, після злиття `lab/1-system` у `main`).
- Commit hash: `TODO` (записати вивід `git rev-parse HEAD` після злиття).

Середовище перевірки:

- .NET SDK 10.0.303 (смуга 10.0.3xx, `rollForward: latestPatch` у `global.json`).
- Docker Compose v5.5.1, образ `postgres:18.4-alpine3.24`.
- PostgreSQL опубліковано на `127.0.0.1:54329`, API — на `http://localhost:5080`.

## 2. Змінений маршрут

Реалізовано точку розширення `GET /api/incidents/severity-summary`, яка в baseline
повертала `501 Not Implemented`.

```text
кнопка «Показати підсумок» у Client/index.html (#summary-button)
  → loadSeveritySummary() у Client/app.js
  → GET /api/incidents/severity-summary
  → IncidentEndpoints.GetSeveritySummaryAsync (Presentation/Endpoints)
  → IncidentQueries.GetSeveritySummaryAsync (Application/Incidents)
  → SecureLabDbContext.Incidents (AsNoTracking + GroupBy + Count)
  → PostgreSQL: таблиця incidents
  → IncidentSeveritySummaryResponse (Presentation/Contracts)
  → JSON response
  → textContent у #summary-list
```

Ключові файли та їхня відповідальність:

| Рівень | Файл | Відповідальність |
|---|---|---|
| Client | `src/SecureLab.Api/Client/index.html` | кнопка, статус-елемент, контейнер результату |
| Client | `src/SecureLab.Api/Client/app.js` | `loadSeveritySummary()`, безпечний вивід через `textContent` |
| Presentation | `Presentation/Endpoints/IncidentEndpoints.cs` | маршрут, allowlist-перевірка `status`, 200 або 400 |
| Presentation | `Presentation/Contracts/IncidentResponses.cs` | `IncidentSeveritySummaryResponse` |
| Application | `Application/Incidents/IncidentQueries.cs` | read-only агрегат, політика нульових груп, порядок, журналювання |
| Data | `Data/SecureLabDbContext.cs` | `DbSet<Incident>`, `ToTable("incidents")` |

### Межі довіри на цьому маршруті

| Межа | Дані | Що не можна припускати | Контроль |
|---|---|---|---|
| браузер → API | `status` у query string | що значення надійшло з нашого `<select>` | `Enum.TryParse` + `Enum.IsDefined` у `GetSeveritySummaryAsync`; невідоме значення дає 400 |
| API → PostgreSQL | умови фільтра та групування | що збережений текст автоматично безпечний | параметризація EF Core, `AsNoTracking()`, проєкція лише в `severity`/`count` |
| API → браузер | JSON агрегату | що право читати entity дає право одержати всі поля | окремий `IncidentSeveritySummaryResponse` без `description`, owner ID, email і коментарів |
| дані response → DOM | текстові значення JSON | що текст можна інтерпретувати як HTML | `document.createElement` + `textContent`; `innerHTML` відсутній (перевіряє автотест) |

## 3. Виконані зміни

Обсяг diff: **7 файлів, 140 доданих рядків, 6 вилучених**. Migration не потрібна —
`Incident.Severity` і seed уже існують.

### 3.1. Контракт

| Властивість | Значення |
|---|---|
| HTTP method | `GET` |
| URL | `/api/incidents/severity-summary` |
| Необовʼязковий параметр | `status` (allowlist: `New`, `Triaged`, `InProgress`, `Resolved`, `Closed`) |
| Request body | відсутній |
| Успішна відповідь | `200 OK`, JSON-масив |
| Поля елемента | `severity`, `count` |
| Некоректний `status` | `400` Validation Problem Details |
| **Політика нульових груп** | **повний перелік рівнів enum; рівень без інцидентів має `count: 0`** |
| **Порядок** | **за критичністю: `Low`, `Medium`, `High`, `Critical`** |

У `tests/http/incidents.http` — десять відтворюваних сценаріїв, серед яких окремо
перевірено порожній (T-03, `?status=Resolved` → `200` з `[]`) і некоректний
(T-05, `?status=Unknown` → `400`) випадки.

Обидва рішення зафіксовано явно, бо вони змінюють видиму відповідь:

- **Політика повного переліку** обрана тому, що клієнт одержує сталу форму відповіді:
  чотири елементи незалежно від даних. За політики «лише наявні групи» рівень `Critical`
  на baseline seed просто зник би з відповіді.
- **Порядок за критичністю** обрано тому, що `Severity` зберігається як текст через
  `HasConversion<string>()`, і сортування в SQL було б лексикографічним. Це перевірено
  прямим запитом до БД:

  ```text
   severity | count
  ----------+-------
   High     |     1
   Low      |     1
   Medium   |     1
  ```

  `ORDER BY severity` повертає `High, Low, Medium` — за абеткою, а не за критичністю.
  Тому агрегат спочатку матеріалізується через `ToListAsync`, і лише потім сортується
  в памʼяті за значенням enum.

### 3.2. Application layer

`IncidentQueries.GetSeveritySummaryAsync` — асинхронний read-only запит:

```csharp
var groups = await query
    .GroupBy(incident => incident.Severity)
    .Select(group => new { Severity = group.Key, Count = group.Count() })
    .ToListAsync(cancellationToken);

var counts = groups.ToDictionary(group => group.Severity, group => group.Count);

var summary = Enum.GetValues<IncidentSeverity>()
    .OrderBy(severity => severity)
    .Select(severity => new IncidentSeveritySummaryResponse(
        severity.ToString(),
        counts.TryGetValue(severity, out var count) ? count : 0))
    .ToList();
```

Починається з `dbContext.Incidents.AsNoTracking()`, групує через `GroupBy`, проєктує
лише `severity` і `count` через `Count()`, виконується через `ToListAsync(cancellationToken)`.
`.Result` і `.Wait()` не використовуються.

### 3.3. Структуроване журналювання

```csharp
logger.LogInformation(
    "Built severity summary for status filter {Status}: {LevelCount} levels, {NonEmptyCount} with incidents",
    status, summary.Count, groups.Count);
```

Шаблон повідомлення відокремлено від значень параметрів. До журналу потрапляють лише
назва фільтра та кількості груп — без описів інцидентів, connection string, cookies і токенів.

### 3.4. Presentation layer

Заготовку `501` замінено на окремий асинхронний метод, який одержує `IncidentQueries`
через DI і приймає `CancellationToken`. Metadata `.ProducesProblem(501)` прибрано,
додано `.Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()` і `.ProducesValidationProblem()`.

### 3.5. Browser client

`loadSeveritySummary()` реалізує три окремі стани:

1. **завантаження** — `«Завантаження…»` до запиту;
2. **порожній результат** — `«Даних немає.»` для порожнього масиву;
3. **безпечна помилка** — `«Не вдалося завантажити підсумок.»` у `catch`,
   без stack trace, SQL та внутрішніх деталей.

Кожен рядок створюється через `document.createElement("li")` і `textContent`.

> На штатному seed підсумок не буває порожнім. Гілка порожнього стану виконається,
> якщо таблиця `incidents` не містить жодного рядка — наприклад, до застосування seed.
> Seed-інциденти для демонстрації не видалялися.

## 4. Перевірка

Виконано після останньої зміни коду. Стенд: PostgreSQL `healthy`, API запущено, seed відновлено.

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | PostgreSQL healthy, API запущено | `GET /health` | 200, API бачить БД | **200**, `{"status":"ready"}`, `application/json` | `docs/http-evidence-after.txt` |
| T-02 | відновлений seed | `GET /api/incidents?status=Triaged` | 200, список за фільтром | **200**, 1 елемент — «Підозрілий лист із вкладенням», `Medium · Triaged` | `docs/http-evidence-after.txt` |
| T-03 | відновлений seed | `GET /api/incidents?status=Resolved` | 200 з порожнім масивом | **200**, `[]` | `tests/http/incidents.http`, `docs/http-evidence-after.txt` |
| T-04 | відновлений seed | `GET /api/incidents/99999999-…-999999999999` | 404 Problem Details | **404**, `application/problem+json`, title «Інцидент не знайдено», є `traceId` | `docs/http-evidence-after.txt` |
| T-05 | відновлений seed | `GET /api/incidents?status=Unknown` | 400 Validation Problem Details | **400**, `application/problem+json`, `errors.status` | `docs/http-evidence-after.txt` |
| T-06 | реалізований етап 3 | `GET /api/incidents/severity-summary` | 200; `Low:1, Medium:1, High:1, Critical:0`; порядок за критичністю | **200**, `[{"severity":"Low","count":1},{"severity":"Medium","count":1},{"severity":"High","count":1},{"severity":"Critical","count":0}]` | `docs/http-evidence-after.txt`, знімок 03 |
| T-06b | реалізований етап 3 | `GET /api/incidents/severity-summary?status=Triaged` | 200; лише `Medium:1` | **200**, `Low:0, Medium:1, High:0, Critical:0` | `docs/http-evidence-after.txt` |
| T-06c | реалізований етап 3 | `GET /api/incidents/severity-summary?status=Unknown` | 400 Validation Problem Details | **400**, `application/problem+json`, `errors.status` | `docs/http-evidence-after.txt` |
| T-07 | API і клієнт запущено | натиснути «Показати підсумок» | UI безпечно показує результат; є стани завантаження, порожнього результату й помилки | **`Рівнів: 4`**, чотири рядки `Low: 1 / Medium: 1 / High: 1 / Critical: 0`; стан помилки — «Не вдалося завантажити підсумок.»; порожній — «Даних немає.» | знімки 03, 04, 05 |
| T-08 | після reset | `--reset-database`, повторити T-02 і T-06 | seed повертає стенд до відомого стану | **виконано**: «Локальні навчальні дані очищено та повторно заповнено seed-значеннями»; T-02 дав той самий один інцидент, T-03 — `[]`, T-06 — `Low:1, Medium:1, High:1, Critical:0`; звірка в БД — 3 рядки (High/Low/Medium по 1) | команда + повторні response |

### Стан до реалізації

`GET /api/incidents/severity-summary` до зміни повертав **501**:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.2",
 "title":"Точку розширення ще не реалізовано",
 "status":501,
 "detail":"Завершіть цей endpoint під час лабораторної роботи № 1."}
```

### Автоматизована перевірка

```text
dotnet build SecureLab.sln --configuration Release
  Build succeeded. 0 Warning(s), 0 Error(s)

dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
  Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 1 s
```

Усі чотири baseline-тести залишилися зеленими, зокрема
`ClientScript_DoesNotUseDangerousInnerHtmlSink`, який не допускає `innerHTML` у `app.js`.

### Read-only звірка з PostgreSQL

```sql
SELECT id, title, severity, status FROM incidents
WHERE id = '20000000-0000-0000-0000-000000000003';
```

```text
                  id                  |                 title                 | severity | status
--------------------------------------+---------------------------------------+----------+--------
 20000000-0000-0000-0000-000000000003 | Перевірка журналу комп'ютерного класу | Low      | New
```

Значення збігаються з полями фактичної JSON-відповіді details-операції.

### Знімки екрана

| Файл | Що доводить |
|---|---|
| `docs/screenshots/01-client-initial.png` | клієнт після завантаження; підсумок ще не запитано |
| `docs/screenshots/02-details-script-as-text.png` | `<script>` в `description` показано як текст, а не виконано як HTML |
| `docs/screenshots/03-summary-loaded.png` | успішний підсумок: `Low: 1`, `Medium: 1`, `High: 1`, `Critical: 0`, статус `Рівнів: 4` |
| `docs/screenshots/04-summary-safe-error.png` | стан безпечної помилки без stack trace і внутрішніх деталей |
| `docs/screenshots/05-summary-empty.png` | стан порожнього результату |
| `docs/screenshots/06-scalar-openapi.png` | контракт у Scalar/OpenAPI |

## 5. Security-сценарій

ЛР 1 не містить навмисно вразливого стану, тому окремий PoC не виконувався.
Практично перевірено один захисний контроль маршруту: seed-інцидент
«Перевірка журналу комп'ютерного класу» має в `description` текст
`Текст <script> має відображатися як текст, а не виконуватися як HTML.`

Спостереження: рядок показано в DOM як звичайний текст; сценарій не виконався.
Причина: `renderIncidentDetails` і `loadSeveritySummary` створюють вузли через
`document.createElement`/`document.createTextNode` і записують значення через
`textContent`, а не перетворюють дані на розмітку.

Залишковий ризик: контроль діє лише в цьому клієнті. Інший HTTP-клієнт одержить
той самий JSON і може обробити його небезпечно — тому серверна перевірка входу
(`Enum.TryParse` + `Enum.IsDefined`) і явна проєкція DTO залишаються обовʼязковими
незалежно від поведінки frontend.

## 6. Висновок

Точку розширення `GET /api/incidents/severity-summary` реалізовано наскрізно: від
кнопки браузерного клієнта до агрегувального запиту EF Core і назад до безпечного
DOM-виводу. Фактичний результат збігається із задокументованим контрактом —
`200 OK` з чотирма рівнями в порядку критичності, `Critical: 0` за політикою повного
переліку, `400` для значення `status` поза allowlist і `404` для відсутнього інциденту.
Автоматизована перевірка після останньої зміни: build без попереджень, 4 з 4 тестів пройдено.

Головний практичний висновок: `200` сам по собі нічого не доводить. Однакові за
класом відповіді (`200` зі списком і `200` з `[]`) означають різне, а різні за класом
(`400` і `404`) розділяють некоректний параметр і відсутній ресурс. Рішення, які не
видно в коді endpoint — політика нульових груп і порядок елементів — довелося зафіксувати
в контракті окремо, бо без них дві коректні реалізації дають різні відповіді.
