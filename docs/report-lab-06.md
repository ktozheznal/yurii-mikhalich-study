# Підсумковий огляд безпеки SecureLab ЛР 6

Рівень: **«Добре»**, baseline 2-A. Дата: **3 листопада 2026 року**. Review-start: `cfe2d6bd7664112994684a03c279cd0a1e90e299`. Наявна гілка: `lab6`. Стан після роботи ідентифікується SHA-256 маніфестом вихідних файлів та результатами тестів. **Commit, merge, tag `v1.0.0` і push не виконувалися за прямою вказівкою користувача.** Нового final commit hash немає; це перевірений незакомічений стан, який користувач закомітить самостійно.

## 1 Executive summary

У власному трекері інцидентів перевірено клієнт, cookie authentication, API, PostgreSQL, конфігурацію, залежності й журнали. Зафіксовано й виправлено дві відтворені знахідки: mutation без antiforgery token змінювала інцидент; unexpected 500 втрачала наявні browser security headers. Для першої додано стандартний ASP.NET Core antiforgery flow на весь чинний cookie mutation perimeter та спільний клієнтський helper; для другої headers установлюються через `Response.OnStarting` поза exception handler. Додатково введено search limiter, збережено правила доступу та повторно виконано регресії ЛР2–5. **Фінальний прогін: 105 passed, 0 failed, 0 skipped; 2 хв 23 с, exit code 0.** Реальний browser retest підтвердив CSRF400/незмінний стан, валідний204/змінений стан, F5create201, logout204→401, safe comment і CORS4173/4174. Основні залишкові ризики: відкриті login/register без antiforgery, локальний HTTP, process-local quotas і відсутність завершального Git оформлення.

## 2 Scope та exclusions

До огляду входять поточні tracked файли репозиторію та нові вихідні файли ЛР6, browser pages, endpoint registration, OpenAPI Development, Identity, серверні правила `IncidentAccess`, EF Core queries, локальна БД, cookie/headers/CORS, rate limits, dependency decisions і безпечні журнали. Деталі системи й зв’язок кожної межі з конкретними tests наведено в [threat-model.md](threat-model.md).

Історія Git повністю не перевірялася. Систематичний secret search стосується current tracked state; фактичні refs або вибіркові історичні перевірки зазначено лише в матеріалах аудиту. Review-start hash і baseline snapshot зберігають походження змін; вони не означають сканування всієї історії.

Виключення: сторонні hosts, real credentials/data, production deployment, сертифікати й реальний TLS/HSTS browser state, distributed Redis limiting, DDoS/load test та advanced extension рівня «Відмінно». Чинні CSP і login limiter з попереднього fixed state збережено й включено до регресії. Третя finding і погоджений advanced variant не входять до обраного рівня.

## 3 System overview і trust boundaries

SecureLab є одним ASP.NET Core застосунком із HTML/Vanilla JS, ASP.NET Core Identity та EF Core/PostgreSQL. Authentication cookie визначає principal; owner і роль не беруться з JSON чи demo headers. Reporter працює зі своїми записами; Analyst має дозволений `New → Triaged`; Admin не має загального bypass. Коментар перевіряється через доступ до батьківського інциденту.

Ключові перевірки меж: browser→API — CSRF, auth, IDOR і CORS; API→PostgreSQL — параметризований пошук, DTO validation і server-selected fields; repository→build — locked restore, dependency і secret audit; environment→configuration — фактичні Production/Development tests; API→logs — безпечні events та trace correlation. Повна таблиця міститься в моделі загроз.

## 4 Methodology та test environment

| Параметр | Фактичне значення |
|---|---|
| ОС та SDK | Windows; .NET SDK `10.0.303` |
| БД | PostgreSQL Docker; Compose project `securelab-lab6-verification`; порт `54333`; БД `securelab` |
| Стан даних | Відтворюваний Development reset/seed; лише штучні Alice/Bob/Morgan/Admin; пароль генерується/передається поза Git |
| Клієнт browser | Headless Chrome/Chromium, реальний local HTTP origin `http://localhost:5080`; окремі cookie contexts |
| Початковий стенд | Копія початкових source: API `5086`; controlled Production error `5087`; `output/lab06/evidence/before.json` |
| Фінальний стенд | Штатний Development API `5080`; controlled Production error `5088`; недосяжний локальний DB target `54399` |
| Додаткова кореляція логів | Окремий Production HTTP процес `localhost:5090` зі scopes; його500 response і ExceptionHandlerMiddleware TraceId зіставлено |
| Integration client | xUnit, WebApplicationFactory/TestServer; HTTPS BaseAddress для cookie policy; Production environment asserted |
| CORS origins | `http://localhost:4173` allowed; `http://localhost:4174` denied; різні origins одного site |
| Перевірений стан | Незакомічене робоче дерево; [source manifest](lab06-evidence/source-manifest.json), [tests](lab06-evidence/tests.txt), [browser evidence](lab06-evidence/fixed.json) |

Негативний контроль проведено до виправлення на локальній копії вихідного стану. `before.json` фіксує `PUT` без token → **204**, Description змінено, `stateUnchanged=false`. Production login із недосяжною БД → **500**; `nosniff`, Referrer-Policy та CSP відсутні. При цьому безпечний ProblemDetails уже мав `traceId`; відсутність trace не є finding.

Команди відтворення з кореня проєкту:

```powershell
./scripts/verify-lab06.ps1 -Rounds 1
# Після тестів за потреби: той самий сценарій із -Serve
dotnet restore SecureLab.sln --locked-mode
dotnet build SecureLab.sln -c Release --no-restore --disable-build-servers
dotnet test SecureLab.sln -c Release --no-build --no-restore --logger "trx;LogFileName=lab06-final.trx" --results-directory TestResults
```

Скрипт ізолює Compose project/volume і port, передає навчальні secrets через environment, виконує reset у Development та відновлює попередні environment variables. Не можна запускати reset у Production. Для final error сценарію не потрібен public debug endpoint: достатньо існуючого login зі штучним password та недосяжною локальною БД. Значення cookie, token, seed password і повні logs не входять у звіт.

| Етап | Exit code і фактичний підсумок |
|---|---|
| Locked restore | Команда відтворення наведена вище; факт/exit останнього restore міститься у dependency audit, а не в `--no-restore` final test |
| Release build | 0; 0 warnings, 0 errors |
| Reset isolated DB | 0; Development reset завершено й seed відновлено до final suite |
| Full test suite | 0; 105 passed, 0 failed, 0 skipped; 2 хв 23 с; `TestResults/lab06-final.trx` |
| Final browser evidence | Capture завершено; [fixed.json](lab06-evidence/fixed.json): CSRF, UI F5/logout, XSS comment, 5 header classes і CORS |
| OpenAPI та logs | [openapi.json](lab06-evidence/openapi.json) містить рівно7unsafe operations; [log-audit.json](lab06-evidence/log-audit.json) підтверджує6сценаріїв,0sentinel leaks і prod trace match |

## 5 Findings summary

| ID | Актив | Вплив | Ймовірність у моделі | Пріоритет | Статус | Retest |
|---|---|---|---|---|---|---|
| F-01 | Інцидент і cookie сеанс | Mutation може пройти без підтвердження наміру користувача; можливий також примусовий logout | Середня: потрібний cookie context; SameSite/CORS зменшують окремі шляхи, але не замінюють серверну перевірку | P2: виправити весь cookie perimeter | Fixed; усі10 integration variants Passed; browser PUT400/400/204 і стан підтверджено | REG-CSRF-01; всі 5 operations × missing/invalid |
| F-02 | Browser response policy на unexpected error | Наявні захисні headers зникають саме на 500 response | Висока за контрольованої exception path | P3: defense-in-depth gap; витік stack trace не спостерігався | Fixed; Production500 фактично має всі3 headers; integration Passed | REG-HEADERS-500 і п’ять response classes |

## 6 Detailed findings

Окремі картки [F-01 CSRF](findings/F-01-csrf.md) і [F-02 error headers](findings/F-02-error-headers.md) містять передумови, before evidence, причину, fix, негативний retest, позитивну регресію та межі. Знахідки отримано на власному source snapshot, а приклади з додаткового архіву використано як довідку. Навчальні screenshot-и автора методички не подаються як результати цього проєкту.

F-01 виправлено `IAntiforgery.GetAndStoreTokens` + `ValidateRequestAsync`. Для JSON minimal APIs використано explicit endpoint filter перед handler; CSRF failure повертає 400 із конкретним title і не має `errors` DTO validation. F-02 виправлено розміщенням OnStarting callback навколо exception pipeline; callback пише headers перед відправленням остаточної відповіді після її можливого очищення exception handler.

## 7 Regression results

Кожний рядок зберігає первинну мету перевірки. Легітимні validation/authorization tests отримують реальний token тим самим cookie client, щоб CSRF 400 не маскував DTO 400 або очікуваний 403. Негативні CSRF clients створюються з `antiforgery:false` і не мають глобально доданого token.

| ID і клас | Конкретний test або процедура | Очікуваний контракт і перевірка стану | Фінальний результат |
|---|---|---|---|
| S02 SQLi | `S02_ControlInput_DoesNotExpandResults`; literal wildcard tests | Контрольний query не розширює вибірку; apostrophe працює; `%`, `_`, `\\` є текстом | Passed у final TRX |
| T01/T02/T03 validation | `Lab02ContractTests` | Valid create201; invalid DTO400 із `errors.<field>`; duplicate409; server fields ігноруються | Passed у final TRX |
| A01–A07 authentication | `Lab03AuthenticationTests` | Anonymous401 без redirect; verified principal; logout204→me401; owner з principal | Passed; browser logout204→401 також підтверджено |
| A03/A04/A06/A08a/A12 authorization | `Lab04AuthorizationTests` | Чужий resource403, snapshot незмінний; Reporter не triage; child access через parent | Passed у final TRX |
| REG-XSS-01 | Browser seed comment та новий description через F5 helper | Коментар із img marker відображається буквально, unsafeElements0/markerNull; новий опис з HTML-like текстом literal/DOM0 | Passed у fixed.json; попередні ЛР5 round-trip докази збережено |
| REG-CSRF-01 | `REG_CSRF_AllCookieMutations_RejectWithoutValidToken_KeepState_ThenAcceptValidPair` | Missing/invalid400 для create/update/delete/triage/logout; стан/кількість/сеанс незмінні; валідна пара201/204/200 із очікуваною зміною | Усі10 variants Passed; browser PUT400/400/204 зі state check |
| REG-CSRF-02 | `REG_CSRF_TokenIsBoundToAuthenticatedIdentity_AndFreshPairWorksAfterNewLogin` | Після Alice→Bob старий token400 і запису немає; fresh token201, owner Bob | Passed у final TRX |
| REG-CORS-01 | Browser 4173/4174; `Lab05BrowserPolicyTests` | Exact allowed origin + credentials; denied browser read; direct HTTP не тотожний browser CORS | Passed;4173 health і credentialed me200 readable;4174 TypeError; direct HTTP200 без ACAO |
| REG-RATE-LOGIN | `LoginQuota_IsSharedAcrossClients_RecoversAfterRealWindow_AndFreshHostIsIndependent` | 5 attempts/min/IP, шоста429 + Retry-After, після реальних61s штатний401; forwarded header не підміняє IP | Passed; real window, не прискорений profile |
| REG-RATE-SEARCH | `REG_RATE_Search_DefaultQuotaUsesTwoIdentityPartitions_SharedClientsAndRealWindowRecovery` | Alice10×200; інший Alice client429; Bob independent10×200 потім429; після61s обидва200; unrelated me200 | Passed; дві principal partitions і real61s |
| REG-HEADERS | `Headers_PersistOnStaticApiValidationAndMissingResource`; Production error test | Static200/API200/validation400/unknown404/controlled500; actual nosniff/no-referrer/CSP | Passed; усі5 класів також зафіксовано у fixed.json |
| REG-AUDIT | Auth event tests; A09; `StatusAudit_RecordsAllowedAnalystAndDeniedReporter_WithoutBodyOrCredentials`;6реальних HTTP сценаріїв | Login204/failed401; Reporter403 без зміни; validation400 без body у логах; Analyst200 New→Triaged; unexpected500 із trace match | Passed; log-audit.json: scenarios6/leaks0; safe-log.txt містить очищені події і prod trace |

Загальний результат: **105 passed, 0 failed, 0 skipped; 2 хв 23 с; exit0**. Первинний machine-readable результат: `TestResults/lab06-final.trx`; безпечний витяг: [tests.txt](lab06-evidence/tests.txt). Результати відносяться до SHA-256 manifest перевіреного дерева, не до нествореного final commit.

## 8 Hardening controls

### Повний mutation perimeter

Зіставлено registrations серверних routes, Development OpenAPI та фактичні client calls. PUT/DELETE/triage опубліковані як API й не мають штатної кнопки в поточному UI; це підтримуваний HTTP contract, а не прихований route чи відсутня CSRF вимога. Logout, create, login/register викликаються клієнтом. Окремих PATCH/batch/admin/comment-write routes у цьому стані немає. GET/HEAD/OPTIONS не змінюють предметні ресурси; token GET видає лише security token/cookie.

| ID | Method і route | Джерела та узгодження | Зміна стану | Cookie auth | Browser accessible | Antiforgery | Regression |
|---|---|---|---|---|---|---|---|
| SC-01 | POST `/api/incidents` | ЛР2/3; source/OpenAPI; auth-lab.js і browser-lab.js | Створення свого Incident | Так | Так | Explicit shared filter | REG-CSRF-01 create |
| SC-02 | PUT `/api/incidents/{id}` | ЛР4; source/OpenAPI; HTTP contract, поточного UI call немає | Title/Description | Так | Так | Explicit shared filter | REG-CSRF-01 update |
| SC-03 | DELETE `/api/incidents/{id}` | ЛР4; source/OpenAPI; HTTP contract, поточного UI call немає | Видалення свого New | Так | Так | Explicit shared filter | REG-CSRF-01 delete |
| SC-04 | POST `/api/incidents/{id}/triage` | ЛР4; source/OpenAPI; HTTP contract, поточного UI call немає | Analyst New→Triaged, history | Так | Так | Explicit shared filter | REG-CSRF-01 triage; A06/A07 |
| SC-05 | POST `/api/auth/logout` | ЛР3; source/OpenAPI; auth-lab.js | Завершення сеансу | Так | Так | Explicit filter до SignOutAsync | REG-CSRF-01 logout |
| SC-06 | POST `/api/auth/login` | ЛР3; source/OpenAPI; auth-lab.js | Початок/заміна сеансу | Ні | Так | Absent; окремий прийнятий risk | A02/A07; login limiter |
| SC-07 | POST `/api/auth/register` | ЛР3; source/OpenAPI; auth-lab.js | Створення Reporter account | Ні | Так | Absent; окремий прийнятий risk | Lab03RegistrationTests |

Фактично отриманий Development `/openapi/v1.json` збережено в [openapi.json](lab06-evidence/openapi.json): він містить рівно сім unsafe method/route, наведені в таблиці. Registrations source та OpenAPI узгоджені. Client calls create/logout/login/register звірено з `auth-lab.js` і `browser-lab.js`; PUT/DELETE/triage залишаються навмисними API-only operations. Зафіксованих phantom routes у переліку немає; відсутність UI кнопки не звільняє HTTP mutation від CSRF.

### Token flow і browser helper

Після login клієнт дістає токен через authenticated GET `/api/antiforgery/token`. Відповідь `{token,headerName}` має `Cache-Control:no-store`; request token надсилається в `X-CSRF-TOKEN`, cookie валідується framework. Authentication cookie: HttpOnly, SameSite=Lax; antiforgery cookie: `SecureLab.Antiforgery`, HttpOnly, SameSite=Strict. Обидві мають SameAsRequest у Development та Always Secure поза ним.

`Client/csrf.js` використовується на `auth-lab.html` і `browser-lab.html`: token тільки в пам’яті; lazy fetch після F5/переходу; очищення після login/logout/401; same-origin restriction; жодного автоматичного повтору mutation. Public login/register явно виключені з cookie-CSRF helper. Старий token іншого principal не приймається. SameSite та CORS залишаються окремими controls.

### Headers і профілі

Поточна enforcement CSP: `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'`. Разом із `nosniff` і `no-referrer` вона встановлюється через OnStarting. Development Scalar має окреме обґрунтоване виключення для своїх inline/CDN resources; у Production OpenAPI/Scalar не реєструються.

| Control | Ризик | Фактична перевірка | Межа |
|---|---|---|---|
| nosniff | MIME sniffing | 5 classes, Dev browser/HTTP та Production controlled500 | Потрібен також коректний Content-Type |
| no-referrer | Передавання URL сторонньому адресату | Ті самі response classes | Не дозволяє класти secrets у URL |
| CSP і frame-ancestors none | Resource injection та embedding | CSP headers; реальний штатний UI; marker лишається текстом | Не замінює safe DOM/authorization |
| CORS | Cross-origin response read | Development browser4173/4174; credentials, exact allowlist | Не authentication і не CSRF control |
| Cookie flags | Частина cookie exposure/cross-site risks | Actual browser HTTP metadata; DevHTTP/DevHTTPS/ProdHTTPS TestServer | HttpOnly не зупиняє XSS requests; TestServer не доводить TLS |
| Login/search limiting | Повторні спроби й витрати ресурсів | Реальні quota/429/Retry-After/61s; два search principals | Process-local; не authentication/authorization/DDoS solution |
| HTTPS/HSTS | Незахищений транспорт | Production TestServer `https://securelab.test`, localhost exclusion; local HTTP diagnostic | Real TLS/HSTS browser deployment не перевірено |

Production відрізняється від Development: Secure Always cookies; reset/startup migrations відсутні без явного Development дозволу; OpenAPI/Scalar і CORS probe вимкнені; HSTS30days лише для HTTPS non-loopback, стандартні loopback exclusions збережено. HTTPS redirect вмикається `Security:HttpsRedirectionEnabled` після налаштування реального listener/proxy; локальна Production HTTP діагностика500 не видається за безпечне розгортання. [fixed.json](lab06-evidence/fixed.json) фіксує всі5 classes з трьома headers і реальну HTTP auth-cookie HttpOnly=true/SameSite=Lax/Secure=false. Tests підтвердили antiforgery cookie HttpOnly/Strict, DevHTTP Secure=false, DevHTTPS/ProdHTTPS Secure=true; HSTS є лише на Production HTTPS non-loopback TestServer.

## 9 Dependency and secret audit

Фактичний dependency audit: [dependency-audit.txt](lab06-evidence/dependency-audit.txt). Версії direct/transitive packages, advisory interpretation і рішення update/retain: [dependency-decisions.md](lab06-evidence/dependency-decisions.md). Межі systematic secret search та класифікація кожного secret-like match: [secret-audit.md](lab06-evidence/secret-audit.md). Пакети не оновлюються навмання, а відсутність знайдених advisories не трактується як доказ відсутності всіх ризиків.

Обов’язковий search perimeter: усі current tracked files через `git ls-files`/`git grep`, `.gitignore`, `.env.example`, appsettings, scripts, lockfiles та project files. Відкриті `local-study-password` у стандартній локальній конфігурації класифікуються як study-only non-production defaults; вони не є seed password користувачів і не повинні повторно використовуватися поза локальним стендом. Змінні на зразок `SeedUsers__Password` або типи `PasswordHash` є назвами config/API, а не значеннями секретів. Реальні credentials/cookies/tokens до доказів не додаються.

Журнали перевірено у двох каналах: safe ProblemDetails для клієнта та protected server diagnostics. Для500 body містить status/title/traceId без exception/SQL/path/credentials; центральний 32-hex сегмент traceId зіставляється з TraceId exception log. У before state traceId уже був присутній. Authentication events використовують verified UserId або unverified, а не введений username/password. Authorization denial та дозволений Analyst status audit містять actor/action/resource/time/trace, а не повний body.

Фактично виконано шість сценаріїв: successful login204, failed login401, denied protected triage403 із незмінним ресурсом, DTO validation400 без body в журналі, привілейований New→Triaged200 та unexpected server error500. [log-audit.json](lab06-evidence/log-audit.json) містить `scenarios:6`, `checkedPasswordTokenCookiesAndBodySentinels:true`, `leaks:0`. Це перевірка конкретних sentinel значень у конкретних діях, а не гарантія безпечності всіх можливих журналів.

[safe-log.txt](lab06-evidence/safe-log.txt) містить очищені події та кореляційний витяг. Для додаткового Production HTTP процесу5090 [diagnostic-response.json](lab06-evidence/diagnostic-response.json) має traceId `00-9c21f4f52eafb87b6a94348f8a689907-ef189c8136cfd0f3-00`; у записі `Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware[1]` фактичний TraceId дорівнює `9c21f4f52eafb87b6a94348f8a689907`. `traceMatches:true` підтверджує збіг середнього сегмента. Це окрема дія зі scopes; початковий final header500 на5088 залишається окремим валідним спостереженням у `fixed.json` із traceId `00-0b623ba5466570097cb98a3159ae1863-23d75641ef878742-00`. Повний TRX також має Passed auth/audit/exception capture tests.

## 10 Residual risks

| Залишковий ризик | Статус і межа | Відповідальний та наступна дія |
|---|---|---|
| Login-CSRF/account creation | Login/register не мають antiforgery; cookie perimeter решти mutations захищено | Власник проєкту: окремо узгодити pre-auth token/origin flow і перевірити account switching |
| HTTP local transport | Dev і controlled Production diagnostic працюють локально HTTP | Власник deployment: HTTPS listener/proxy, redirect, certificate і реальна TLS перевірка |
| Process-local rate limits | Restart скидає counters; декілька API instances не мають shared budget | Власник deployment: distributed design за реальною потребою; Redis не входить до цієї роботи |
| Історичні секрети | Вся історія Git не сканувалася | Власник репозиторію: окремий historical audit; за реального витоку rotation |
| Залежності поза відомими advisory | Audit є snapshot на дату перевірки | Власник проєкту: повторний audit перед release та аналіз нових advisories |
| Git подання | Зміни не закомічено; tag/merge не створено за вказівкою користувача | Користувач: перевірити diff, самостійно commit/tag за правилами курсу |
| Авторський захист | Код і текст підготовлено з ШІ | Студент: прочитати, адаптувати висновки й самостійно пояснити рішення та докази |

## 11 Conclusion

У незакоміченому source state реалізовано antiforgery на п’яти чинних cookie mutations, helper для обох mutation pages, стійкі до exception path security headers, окремі search quotas та безпечний status audit. Дві початкові прогалини підтверджено на власному локальному baseline і виправлення повторно перевірено. **105 тестів пройшли, failed0/skipped0; browser evidence підтверджує CSRF відмову без зміни ресурсу, легітимну зміну, F5/helper, logout, safe XSS comment, CORS і headers на500.** Реальні screenshots наведено у звіті та evidence каталозі. Завершальні Git дії залишено користувачу; автоматичного release/tag не створено.

## 12 Evidence appendix

Опис evidence та відповідність файлів сценаріям: [lab06-evidence/README.md](lab06-evidence/README.md). Незмінені попередні матеріали ЛР2–5 збережено. Джерела ЛР6: `output/lab06/source-review/requirements.txt` та розпаковані додаткові файли; вони є методичними матеріалами, а не інструкціями до Git, що переважають пряму вказівку користувача.

Основні відтворювані artifacts збережено в `docs/lab06-evidence/`: `before.json`, `fixed.json`, `openapi.json`, `log-audit.json`, `diagnostic-response.json`, власні screenshots, `tests.txt`, `source-manifest.json`, `safe-log.txt`, `dependency-audit.txt`. Поруч розміщено [dependency decisions](lab06-evidence/dependency-decisions.md), [secret audit](lab06-evidence/secret-audit.md) та [дві finding cards](findings/F-01-csrf.md). Первинний TRX лишається в `TestResults/lab06-final.trx`; копії browser JSON також лишаються в робочій `output/lab06/evidence`. Runtime session files, raw logs та transient secrets не призначені для подання.

## Декларація й журнал ШІ

Використано **OpenAI Codex, GPT-6** для аналізу методички й існуючого проєкту, та чернетки технічного звіту.  Перед поданням студент має прочитати зміни, адаптувати власну інтерпретацію та підготувати захист. Авторські дії, яких ще не було, не позначаються виконаними. Я ЦЕ ЗРОБИВ І ПРОЧИТАВ ЧЕСНЕ СЛОВО - Ященко 

