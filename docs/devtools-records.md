# Записи DevTools Network (CP-02)

Записи зроблено з власного запуску стенда. Значення `traceId` і тривалість
належать саме цьому запуску й не переносяться з чужих прикладів.

- Дата й час запуску: 2026-09-09, `Wed, 09 Sep 2026 19:06:23 GMT` за заголовком `date` відповіді
- Браузер і версія: Google Chrome 152.0.0.0, Windows (`sec-ch-ua-platform: "Windows"`)
- URL клієнта: `http://localhost:5080/`
- Remote Address: `[::1]:5080` — зʼєднання йде на IPv6-loopback

---

## Гілка 1. Успішний перегляд деталей інциденту

**Дія в UI:** у фільтрі вибрано `New` → «Застосувати» → відкрито картку
«Перевірка журналу компʼютерного класу».

| Поле | Значення з мого запуску |
|---|---|
| Request method | `GET` |
| Повний Request URL | `http://localhost:5080/api/incidents/20000000-0000-0000-0000-000000000003` |
| Status code | `200 OK` |
| Content-Type (response) | `application/json; charset=utf-8` |
| Accept (request header) | `application/json` |
| Request body | відсутній (властивість цього контракту, а не правило для всіх HTTP-запитів) |
| Server | `Kestrel` |
| Transfer-Encoding | `chunked` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `no-referrer` |
| `Sec-Fetch-Mode` / `Sec-Fetch-Dest` / `Sec-Fetch-Site` | `cors` / `empty` / `same-origin` |
| Тривалість запиту | `TODO` мс — вкладка Timing |

**Повні response headers, зафіксовані у вкладці Headers:**

```http
HTTP/1.1 200 OK
content-type: application/json; charset=utf-8
date: Wed, 09 Sep 2026 19:06:23 GMT
referrer-policy: no-referrer
server: Kestrel
transfer-encoding: chunked
x-content-type-options: nosniff
```

**Request headers (скорочено, без приватних даних):**

```http
GET /api/incidents/20000000-0000-0000-0000-000000000003 HTTP/1.1
host: localhost:5080
accept: application/json
accept-encoding: gzip, deflate, br, zstd
sec-fetch-dest: empty
sec-fetch-mode: cors
sec-fetch-site: same-origin
```

У записі відсутні `Cookie` та `Authorization` — застосунок не використовує
ні сесій, ні автентифікації, тому вилучати з доказів не було чого.

**Чим доведено, що запит сформував JavaScript, а не адресний рядок.**
Трійка `sec-fetch-dest: empty`, `sec-fetch-mode: cors`, `sec-fetch-site: same-origin`
означає програмний виклик `fetch()` із того самого походження. Навігація браузера
дала б `sec-fetch-dest: document` і `sec-fetch-mode: navigate`. Отже, запит
породив саме `apiFetch()` у `Client/app.js`, викликаний обробником кліку по картці.

**`accept: application/json`** надіслав не браузер за замовчуванням, а наш код:
`apiFetch` явно встановлює цей заголовок. Браузерна навігація надіслала б
`accept: text/html,...`.

**`x-content-type-options: nosniff`** — відповідь заборонено інтерпретувати
всупереч оголошеному `Content-Type`. Разом із перевіркою самого `Content-Type`
це відповідь на типовий хибний висновок «досить прочитати status code»:
запасний маршрут міг би повернути `200` з HTML, і саме заголовки це виявляють.

**Response body** (зафіксовано з вкладки Response):

```json
{
  "id": "20000000-0000-0000-0000-000000000003",
  "title": "Перевірка журналу комп'ютерного класу",
  "description": "Текст <script> має відображатися як текст, а не виконуватися як HTML.",
  "severity": "Low",
  "status": "New",
  "occurredAtUtc": "2026-08-03T07:45:00+00:00",
  "createdAtUtc": "2026-08-03T08:00:00+00:00",
  "ownerDisplayName": "Аліса Коваль",
  "comments": []
}
```

**Зіставлення з `IncidentDetailsResponse`.** Відповідь містить рівно дев'ять
дозволених полів контракту. У ній **немає** `ownerUserId`, немає email власника
й немає внутрішніх коментарів — попри те, що entity `Incident` ці дані має, а
`StudyUser` має email. Це доводить, що працює явна проєкція в
`IncidentQueries.GetDetailsAsync`, а не серіалізація entity: інакше зайві поля
опинилися б у JSON автоматично.

`comments: []` — порожній масив, а не `null`: коментарів у цього seed-інциденту
немає, але контракт колекції збережено.

**Що доводить цей запис:** дія в UI перетворилася на конкретну пару
method + URL; відповідь має контракт деталей одного інциденту й приходить
як `application/json`, а не як HTML запасного маршруту.

---

## Гілка 1b. Другий інцидент — перевірка контракту коментарів

Той самий endpoint, інший ресурс: `20000000-0000-0000-0000-000000000002`
(«Невідома спроба входу»). Записано о `Wed, 09 Sep 2026 19:07:43 GMT`.

| Поле | Значення |
|---|---|
| Request method | `GET` |
| Повний Request URL | `http://localhost:5080/api/incidents/20000000-0000-0000-0000-000000000002` |
| Status code | `200 OK` |
| Content-Type | `application/json; charset=utf-8` |
| Sec-Fetch (dest / mode / site) | `empty` / `cors` / `same-origin` |

**Response body:**

```json
{
  "id": "20000000-0000-0000-0000-000000000002",
  "title": "Невідома спроба входу",
  "description": "Система зафіксувала вхід до облікового запису з нового пристрою.",
  "severity": "High",
  "status": "InProgress",
  "occurredAtUtc": "2026-08-02T14:10:00+00:00",
  "createdAtUtc": "2026-08-02T14:20:00+00:00",
  "ownerDisplayName": "Боб Мельник",
  "comments": [
    {
      "id": "30000000-0000-0000-0000-000000000002",
      "authorDisplayName": "Морган Литвин",
      "text": "Розпочато перевірку журналу автентифікації.",
      "createdAtUtc": "2026-08-02T14:40:00+00:00"
    }
  ]
}
```

**Що додає цей запис до гілки 1.** У попередньому інциденті `comments` був
порожній, тому контракт вкладеної колекції не перевірявся. Тут коментар є, і
видно, що він теж проєктується, а не серіалізується: елемент має рівно чотири
поля `IncidentCommentResponse` — `id`, `authorDisplayName`, `text`, `createdAtUtc`.
У ньому **немає** ідентифікатора автора, немає email автора й немає самого
прапорця `IsInternal`, хоча entity `IncidentComment` їх має. Автор представлений
лише відображуваним імʼям.

**Чого цей запис НЕ доводить.** У baseline seed обидва коментарі мають
`IsInternal = false`, тому жоден коментар не було відфільтровано. Отже, дані
не демонструють роботу фільтра `.Where(comment => !comment.IsInternal)` у
`IncidentQueries.GetDetailsAsync` — цей контроль підтверджується лише читанням
коду. Щоб перевірити його даними, знадобився б seed із внутрішнім коментарем.

**Ще одна відсутність, помітна в контракті.** Сутність `IncidentStatusHistory`
існує в моделі й має рядок для інциденту Аліси, але в details-відповіді немає
жодного поля історії статусів. Контракт віддає лише те, що описано в
`IncidentDetailsResponse`, а не все, що звʼязано з entity.

---

## Гілка 2. Відсутній ресурс

**Дія:** у DevTools Console виконано

```js
void fetch("/api/incidents/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
```

UUID синтаксично коректний, тому маршрутне обмеження `:guid` його пропускає,
але такого інциденту немає в seed.

| Поле | Значення з мого запуску |
|---|---|
| Request method | `GET` |
| Повний Request URL | `http://localhost:5080/api/incidents/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa` |
| Status code | `404 (Not Found)` |
| Content-Type (response) | `application/problem+json` |
| Date | `Wed, 09 Sep 2026 19:10:21 GMT` |
| Server | `Kestrel` |
| Transfer-Encoding | `chunked` |
| `X-Content-Type-Options` | `nosniff` |
| `Referrer-Policy` | `no-referrer` |
| `traceId` з Problem Details | `00-4d9f74beaba4a82234048b7d95b30834-cfdab6bbc89b416c-00` |

**Response headers, зафіксовані у вкладці Headers:**

```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json
Date: Wed, 09 Sep 2026 19:10:21 GMT
Referrer-Policy: no-referrer
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
```

**Ключове порівняння з успішною гілкою.** Той самий endpoint, той самий метод,
той самий формат ідентифікатора — але **інший media type**:

| | Успіх (гілка 1) | Відсутній ресурс (гілка 2) |
|---|---|---|
| Status | `200 OK` | `404 Not Found` |
| Content-Type | `application/json; charset=utf-8` | `application/problem+json` |

Помилка має власний контракт (RFC 9457 Problem Details), а не є «відповіддю
без даних». Саме тому status code треба читати **разом** із `Content-Type`:
запасний маршрут міг би повернути `200` з `text/html`, і відрізнити це від
справжньої відповіді API можна лише за media type.

Захисні заголовки (`nosniff`, `no-referrer`) присутні в обох гілках однаково —
відповідь про помилку не є винятком із політики.

**Вивід Console:**

```text
> void fetch("/api/incidents/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")
undefined
VM373:1  GET http://localhost:5080/api/incidents/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa 404 (Not Found)
```

`undefined` надрукував оператор `void` — він відкидає Promise, щоб Console не
показувала його замість запису в Network. Позначка `VM373:1` означає, що запит
породив скрипт, обчислений у Console, а не файл `app.js`.

**Response body:**

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Інцидент не знайдено",
  "status": 404,
  "detail": "Інцидент 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' не існує.",
  "traceId": "00-4d9f74beaba4a82234048b7d95b30834-cfdab6bbc89b416c-00"
}
```

**Розбір `traceId`.** Це значення у форматі W3C `traceparent`:

```text
00 - 4d9f74beaba4a82234048b7d95b30834 - cfdab6bbc89b416c - 00
│         │                                   │             └ прапорці
│         │                                   └ span-id (16 hex) — конкретна операція
│         └ trace-id (32 hex) — наскрізний ідентифікатор цього запиту
└ версія формату
```

Значення унікальне для кожного запиту: повторний виклик того самого URL дасть
інший `traceId`. Саме тому його не можна взяти з чужого запуску — він
привʼязує запис у Network до конкретного рядка в журналі сервера.

**Поле `type`** посилається на розділ 15.5.5 RFC 9110 — це визначення `404 Not Found`.
Тобто відповідь не просто має код помилки, а машиночитано вказує на його специфікацію.

**`detail` не розкриває внутрішніх деталей:** повідомлення називає лише той
ідентифікатор, який надіслав сам клієнт, і не містить ні SQL, ні stack trace,
ні структури таблиць.

**Що доводить цей запис:** відсутній ресурс не маскується під успіх.
Це `404`, а не `400`: параметр коректний за формою, немає саме ресурсу.
Порівняйте з `?status=Unknown`, який дає `400` — там некоректний сам параметр.

---

## Гілка 3. Безпечний показ `<script>` (візуальна перевірка)

У відкритій картці «Перевірка журналу компʼютерного класу» поле `description`
містить текст `Текст <script> має відображатися як текст, а не виконуватися як HTML.`

- Чи показано `<script>` як звичайний текст: `TODO (так / ні)`
- Чи зʼявилися помилки або виконаний скрипт у вкладці Console: `TODO`

**Що доводить:** дані з PostgreSQL не стають довіреним HTML лише тому, що вже
збережені в системі. Вузли створюються через `document.createElement`, текст
записується через `textContent`.

---

## Зіставлення з кодом

Запит, зафіксований у Network, обробляють:

| Рівень | Символ | Файл |
|---|---|---|
| Endpoint | `MapGet("/{id:guid}", GetDetailsAsync)` | `Presentation/Endpoints/IncidentEndpoints.cs` |
| Endpoint | `IncidentEndpoints.GetDetailsAsync` | там само — перетворює результат на `200` або `404` |
| Application | `IncidentQueries.GetDetailsAsync` | `Application/Incidents/IncidentQueries.cs` — `AsNoTracking`, `Where`, проєкція, `SingleOrDefaultAsync` |
| Data | `DbSet<Incident> Incidents`, `ToTable("incidents")` | `Data/SecureLabDbContext.cs` |
| Таблиця | `incidents` | PostgreSQL |

Read-only звірка з БД (виконано):

```text
                  id                  |                 title                 | severity | status
--------------------------------------+---------------------------------------+----------+--------
 20000000-0000-0000-0000-000000000003 | Перевірка журналу комп'ютерного класу | Low      | New
```

Значення збігаються з полями фактичної JSON-відповіді.

---

> **Перед додаванням до Git:** якщо зберігали `Copy as fetch` або `Copy as cURL`,
> вилучіть `Cookie`, `Authorization`, session identifiers та інші приватні дані.
