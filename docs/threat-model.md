# Модель загроз SecureLab ЛР 6

Дата огляду: 3 листопада 2026 року. Обраний рівень: «Добре», спільний baseline 2-A. Початок огляду: `cfe2d6bd7664112994684a03c279cd0a1e90e299`; робота продовжує наявну гілку `lab6`. Підсумок стосується перевіреного робочого дерева; commit і tag не створювалися за прямою вказівкою користувача.

## Система та активи

| Елемент | Фактична характеристика |
|---|---|
| Середовище | Windows, .NET SDK `10.0.303`, ASP.NET Core, EF Core, PostgreSQL у Docker Compose |
| Навчальна БД | Compose project `securelab-lab6-verification`, `127.0.0.1:54333`, БД `securelab`; reset виконується лише в Development |
| Клієнт | HTML і Vanilla JS; `auth-lab.html`, `browser-lab.html`, головний трекер; штатний origin `http://localhost:5080` |
| Сеанс | ASP.NET Core Identity, authentication cookie, окремі штучні Alice, Bob, Morgan і Admin |
| Конфігурація | tracked non-secret defaults, `.env.example`, змінні середовища; seed password передається поза Git |
| Діагностика | ProblemDetails для клієнта; серверні diagnostic logs та структуровані authentication/authorization/status events |
| Критичні активи | Credentials і cookie сеансу; вміст власних інцидентів; внутрішні коментарі; дозволений перехід `New → Triaged`; конфігураційні секрети; цілісність журналів |
| Наявні controls | DTO validation; параметризовані EF-запити; `IncidentAccess`; safe DOM `textContent`; CORS allowlist; enforcement CSP; cookie flags; antiforgery; login/search limiting |

## Суб’єкти та точки входу

Анонімний відвідувач може відкрити сторінки й надіслати login/register. Reporter працює зі своїми інцидентами; Analyst читає доступні для аналізу записи та виконує тільки дозволений перехід статусу. Admin не отримує неявного обходу правил інцидентів. Локальний розробник має доступ до конфігурації, тестів і БД, тому належить до довіреної частини навчального стенда.

Недовірені входи: HTTP path/query/JSON, поля форм, сторонній browser origin, клієнтські заголовки, дані інцидентів і коментарів, які повертаються до DOM. Окремі входи до процесу розробки: restore NuGet, tracked файли й змінні середовища. `X-Demo-UserId`, `X-Forwarded-For`, `ownerUserId`, `role` і `status` у зайвих полях JSON не надають повноважень.

## Схема меж довіри

```text
Недовірений браузер або сторонній origin
     | HTTP, cookie, JSON, X-CSRF-TOKEN
     v
ASP.NET Core API і Identity
     | EF Core, scoped queries, server-selected owner/status
     v
Локальний PostgreSQL у Docker

Git tracked files ------> restore і build ------> API
Environment variables -> configuration --------> API
API -------------------> protected diagnostic та audit logs
```

## Очікувані властивості та конкретні перевірки

| Межа | Загроза і очікувана властивість | Конкретна перевірка |
|---|---|---|
| browser → API | Cookie не доводить усвідомленість mutation; без правильної antiforgery-пари стан і logout не змінюються | `REG_CSRF_AllCookieMutations_RejectWithoutValidToken_KeepState_ThenAcceptValidPair`; 5 operations × missing/invalid; після відмови snapshot/лічильник/сеанс, потім валідна дія |
| browser → API | Підміна користувача або ID не надає доступу до чужого ресурсу | A01/A03 ЛР3; A03/A04/A06/A08a/A12 ЛР4; HTTP 401/403 і незмінний стан |
| API → PostgreSQL | Query input не стає SQL; server-managed fields не беруться з клієнта | `S02_ControlInput_DoesNotExpandResults`, literal `%/_/\\` search; `T01_ValidCreate_NormalizesDateAndIgnoresServerManagedFields`; T02 validation |
| PostgreSQL/API → DOM | Збережений текст не перетворюється на виконуваний HTML | `REG-XSS-01`; safe `renderText` і `textContent`; реальний browser marker/DOM evidence у фінальному наборі |
| сторонній origin → browser API | Дозвіл читання response обмежений точним Development origin | `REG-CORS-01`: browser `4173` дозволено, `4174` заблоковано; CORS tests звіряють headers і збереження 401/403 |
| repository → build | Відомі залежності, відтворюваний lockfile, відсутність неврахованих секретів у current tracked files | `dotnet restore SecureLab.sln --locked-mode`; dependency audit direct+transitive; таблиці рішень і класифікації збігів у матеріалах аудиту |
| environment → configuration | Development reset не переходить у Production; secure flags і error handling залежать від фактичного profile | `AntiforgeryCookieAndCachePolicy_MatchActualProfile`, cookie tests ЛР3, `ProductionUnexpectedError_IsSafeProblemDetailsWithTraceAndHeaders`; Production environment перевірено assertion |
| API → diagnostic/audit logs | Діагностика корелює з відповіддю і не друкує credentials або request body; дозволений/заборонений статусний перехід розрізняються | `AuthenticationEvents_RecordSuccessFailureLogoutAndLimit_WithoutCredentials`, A09 audit, `StatusAudit_RecordsAllowedAnalystAndDeniedReporter_WithoutBodyOrCredentials`, контрольований 500 |
| користувач → ресурси API | Один principal не витрачає search quota іншого; після реального вікна доступ відновлюється | `REG_RATE_Search_DefaultQuotaUsesTwoIdentityPartitions_SharedClientsAndRealWindowRecovery`; Alice/Bob, 10/min, 429, Retry-After, 61 s; наявний login limiter 5/min перевірено окремо |

Фактичний результат кожного фінального сценарію наведено в [security-review.md](security-review.md), а не в очікуваннях цієї моделі. Підсумок:105passed/0failed/0skipped; browser capture підтвердив CSRF і CORS; actual OpenAPI має7unsafe operations. Окремий log/audit capture охопив6сценаріїв без sentinel leaks і зіставив Production5090 response trace з ExceptionHandlerMiddleware.

## Межі та виключення

Огляд охоплює власний локальний стенд зі штучними даними. Повний pentest сторонніх систем, реальний production deployment, реальні користувачі, TLS-сертифікати, поведінка HSTS у справжньому HTTPS-браузері й усі історичні Git blobs не перевірялися. Redis у проєкті відсутній; distributed limiter та advanced extension не входять до рівня «Добре».

Login і register занесено в mutation perimeter, але вони не авторизуються вже наявною authentication cookie й не захищені новим CSRF-фільтром. Login-CSRF та небажана реєстрація залишаються окремими ризиками. SameSite і CORS не подаються як їх повне усунення. Два localhost порти доводять відмінність origins, а не cross-site SameSite behavior.

Фактичний локальний HTTP не забезпечує захищеного транспорту. Production TestServer із HTTPS BaseAddress перевіряє application policy та cookie attributes, але не TLS. Після розгортання потрібні HTTPS listener або довірений proxy, налаштування redirect і повторна зовнішня перевірка.
