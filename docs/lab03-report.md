# Звіт до лабораторної роботи № 3

**Тема:** Автентифікація та безпечна робота з обліковими даними.
**Обраний рівень:** «Добре», включно з обов'язковою частиною «Задовільно».
**Ященко Микола** 

## 1. Початковий стан і відтворення

Роботу виконано в кумулятивному проєкті SecureLab, baseline 2-A, на гілці
`lab3` за власним вибором назви. У методичці рекомендована назва
`lab/3-authentication`; фактично використано `lab3`.

- Успадкований тег `v0.2.0`: `db398f7b43033bb0c993dcae079314524c4f88c1`.
- Commit на вході до ЛР 3: `a6d7fdba375288ffca1ba63a210cbf80f96c15af`.
- Scaffold: `lab-03-start-v1`, receipt: `.scaffolds/lab-03.json`.
- Інфраструктуру перенесено вручну після перевірки контрольних сум наданих
  файлів: installer не допускав overwrite зміненого `DatabaseBootstrap.cs`.
  Збережено виправлені endpoints/validation/search ЛР 2 і виклик `Lab02Seed`.
- ОС: Windows; SDK: `.NET 10.0.303`; PostgreSQL: образ із `infra/compose.yaml`.

Команда перевірки з кореня репозиторію:

```powershell
./scripts/verify-lab03.ps1
```

Скрипт піднімає окремий Compose project `securelab-lab3-verification` з власним
volume і портом `54330`, відновлює locked dependencies, виконує Release build,
два `--reset-database` і повні тести після кожного. Пароль seed генерується
тільки в пам'яті процесу та передається через `SeedUsers__Password`.
Звичайна БД на порту `54329` цими reset не змінюється.

**Фактично:** Release build успішний, 0 warnings / 0 errors. Після першого
reset: **50 passed, 0 failed, 0 skipped**. Після другого reset:
**50 passed, 0 failed, 0 skipped**. Обидва запуски включали регресії ЛР 2.
Безпечний витяг із TRX: `docs/lab03-tests.txt`; оригінали:
`TestResults/lab03-round-1.trx` і `TestResults/lab03-round-2.trx` (поза Git).

Після кожного reset повторно проходили реєстрація та перевірки сталих Guid,
ролей і власників. Збережено Alice/Bob/Morgan/Admin, Reporter/Analyst/Administrator,
власників `AliceIncidentId` і `BobIncidentId`, seed-записи пошуку ЛР 2.
Hash і security stamp містять випадковість, тому їхню побайтову однаковість
після reset не заявляю; відтворюється логічний навчальний стан.

## 2. Реалізація та межа DTO

Використано фіксований профіль **ASP.NET Core Identity + EF stores + cookie**
на схемі `IdentityConstants.ApplicationScheme`. Єдиний user store -
`ApplicationUser : IdentityUser<Guid>` у `identity_users`.
Доменні FK залишаються Guid та посилаються на цей тип. Legacy `StudyUser`
не зареєстрований у поточній EF-моделі й не є другим сховищем користувачів.

| Endpoint | Поведінка та дозволені поля |
|---|---|
| `POST /api/auth/register` | `UserRegistrationRequest`: userName, email, displayName, password. `201` і `RegisteredUserResponse`: id, userName, displayName. ID генерує сервер, роль завжди Reporter |
| `POST /api/auth/login` | `UserLoginRequest`: userName, password. Стандартний `PasswordSignInAsync`; успіх `204`, невідомий user або неправильний password - однаковий `401` |
| `GET /api/me` | `.RequireAuthorization()`. `CurrentUserResponse`: userId, userName, role, лише з claims principal; анонімно `401` |
| `POST /api/incidents` | `.RequireAuthorization()` на фактичній реєстрації POST у `Lab02Endpoints`. `CreateIncidentRequest`: title, description, severity, occurredAtUtc; owner із NameIdentifier claim, status New і ID задає сервер |
| `POST /api/auth/logout` | `.RequireAuthorization()`, стандартний `SignOutAsync`, `204`; повторні me/create тим самим клієнтом дають `401` |

Register перевіряє обов'язковість полів, довжини 64/254/120/128, email format
і мінімум 10 символів password; стандартні username/password validators Identity
перевіряють решту правил. Password не обрізається та не нормалізується.
Некоректна реєстрація повертає безпечний `400`, повторне username або email -
послідовний `409`, включно з повторними спробами та зміною регістру.
Account і роль записуються в одній транзакції. У response немає Identity entity,
hash, stamp, password або cookie value.

Зайве `ownerUserId` у JSON ігнорується, бо не входить до DTO. В A-05 саме
**Bob** створює запис із підставленим owner Alice; перевірка читає фактично
збережений `Incident.OwnerUserId` і підтверджує Bob. Поля id/status/role теж
не керують серверним станом. Попередня validation і параметризований search
ЛР 2 збережені; старі POST-тести тепер входять через cookie.

## 3. Пояснення механізму для захисту

**Маршрут довіри:** запит login → `SignInManager.PasswordSignInAsync` →
стандартна перевірка password Identity → захищена application cookie →
браузер автоматично додає cookie → authentication handler перевіряє її →
claims у `HttpContext.User` → authorization middleware перевіряє вимогу входу →
endpoint бере поточного користувача та власника з principal.

Password зберігається як стандартний Identity verifier у `PasswordHash`,
а не відкрито і не як шифротекст для подальшого розшифрування.
`UserManager.CreateAsync` застосовує стандартний password hasher з salt та
обчислювальною вартістю; verification виконує framework. Власне хешування не
реалізовано. Тест перевіряє `HasPasswordAsync` і `CheckPasswordAsync` без
друку значення hash. Response DTO ніколи не включає це поле.

**Чому UserId у body/header не є доказом особи:** клієнт повністю контролює
ці значення та може підставити Guid іншого користувача. Guid лише називає
особу. Доказ для цього застосунку - валідний серверний cookie-сеанс після
стандартної перевірки пароля. Тому `/api/me` не читає `X-Demo-UserId`, а create
не приймає власника з JSON. Якщо обов'язковий Guid claim відсутній у вже
authenticated principal, endpoint повертає `500`, без fallback до client ID.

Ідентифікація - заява username; автентифікація - підтвердження особи;
авторизація - рішення, чи дозволена операція. `401` означає відсутність
підтвердженої особи, `403` - відмову вже authenticated користувачу.
Cookie handlers явно задають HTTP `401`/`403` замість `302`; фактичні
анонімні відмови без Location та HTML redirect перевірено тестами.
Об'єктні права на вже наявний чужий інцидент належать до ЛР 4.

## 4. Cookie, rate limit, події та обмеження

Налаштування cookie містяться в `Lab03Identity.AddLab03Identity`:
`HttpOnly=true`, `SameSite=Lax`, `SecurePolicy=SameAsRequest` у Development,
`Always` поза Development. Login використовує `isPersistent:false`.

| Фактичний сценарій Set-Cookie | HttpOnly | Secure | SameSite | Зберігання |
|---|---|---|---|---|
| Development + HTTP | є | немає | Lax | session, без expires/max-age |
| Development + HTTPS | є | є | Lax | session |
| Production + HTTPS | є | є | Lax | session |
| Production + HTTP | є | є | Lax | session; cookie не призначена для HTTP |

Усі чотири сценарії перевірено за **реальними flags відповіді** у TestServer,
не лише за options. HTTPS у TestServer моделює request scheme; це не доказ
налаштованого TLS-сертифіката чи production deployment. Браузерний запуск був
локальним HTTP і сам по собі не доводить Secure.

HttpOnly обмежує читання cookie JavaScript; Secure обмежує передавання HTTPS;
Lax зменшує частину cross-site надсилань. Строк authentication ticket окремо
не змінено: стандартні 14 днів зі sliding expiration; session cookie без
expires не гарантує втрату сеансу після закриття браузера. Logout видаляє cookie
у поточного клієнта через Identity; відкликання раніше скопійованої cookie або
інших активних сеансів тут не реалізовано.

**Login rate limit:** fixed window, 5 запитів / 60 секунд, QueueLimit=0,
надлишкові запити `429` з `Retry-After`. Ключ - серверна `RemoteIpAddress`,
а не username/password і не довільний forwarded header. Такий вибір не дає
блокувати окремий акаунт тільки знанням його імені, але об'єднує людей за NAT.
У TestServer null IP відповідає спільному ключу `unknown`; новий HttpClient
не очищує quota. Автотест перевірив п'ять `401`, шостий `429`, доступність
публічного GET, незалежну quota нового host і відновлення після 61 секунди.
Ліміт in-memory, локальний для одного процесу; розподілений захист не заявляю.

**Події автентифікації:** login success/failure/rate-limited і logout success.
Поля: `EventType`, `TimestampUtc`, `Result`, `UserId`, `TraceId`.
UserId записується лише після підтвердження особи; для відмов - `unverified`.
Не записуються password, hash, cookie, token, raw Authorization, username
невдалої спроби або весь request body. Автотест перевіряє фактичні структуровані
події та allowlist їхніх полів; EF sensitive-data logging не вмикалося.

**Account enumeration:** login має однаковий зовнішній `401` для unknown user
та wrong password; однаковий час виконання не гарантується. Register залишається
каналом перебору: `409` відрізняє зайняті username/email від доступних. Це
навчальний компроміс із зрозумілим duplicate-контрактом; login limiter не
поширюється на register. Публічні GET інцидентів також показують дозволене
ім'я автора; повна object authorization вводиться в ЛР 4.

**Залишковий CSRF-ризик:** cookie надсилається браузером автоматично.
HttpOnly, SameSite та CORS не становлять повної CSRF-моделі для всіх операцій,
що змінюють стан. Antiforgery flow у цій ЛР не реалізовано; це явний навчальний
борг до ЛР 6. Поглиблення «Відмінно» не входять до обраного рівня.

## 5. Таблиця доказів

| ID | Дія | Фактично | Доказ |
|---|---|---|---|
| T-01 | Коректний register, підставлені id/role/hash | `201`; safe DTO; власний ID і Reporter; стандартний verifier працює | `Lab03RegistrationTests.T01_Register_UsesStandardVerifierAndServerManagedIdAndRole` |
| T-02 | Invalid register та duplicate name/email | `400` / `409`, без частково створеного акаунта; duplicate повторено двічі | `T02_InvalidRegistration…`, `DuplicateRegistration…` |
| T-03 | Модель та verifier | Є `ApplicationUser.PasswordHash`; raw password не зберігається; verification через Identity | T-01, `Lab03SeedTests`, опис моделі вище; без дампа |
| A-01 | Anonymous me і create з підставленим demo header | `401`, немає Location/HTML redirect | `Lab03AuthenticationTests.A01…` |
| A-02 | Login Alice → me | `200`: Alice ID, username alice, Reporter; рівно 3 safe fields | `A02…` |
| A-03 | Alice cookie + Bob X-Demo-UserId | Профіль лишився Alice, результат не змінився | `A03…` |
| A-04 | Bob create без owner | `201`, persisted owner Bob | `A04…` |
| A-05 | Bob create з forged owner Alice | `201`, persisted owner Bob, status New і серверний ID | `A05…` |
| A-06 | Logout → me/create тим самим клієнтом | Logout `204`, обидва protected requests `401` | `A06…` |
| A-07 | Unknown user / wrong password | Однакові `401`, content type та публічні problem fields; без auth cookie | `A07…` |
| S-01 | Огляд response/log/DTO/client/diff | Safe fields; події без credentials; паролі очищені в UI; storage/Console writes відсутні в коді | `AuthenticationEvents_Record…`, T-01/A-02, огляд коду |
| Cookie flags | Development/Production × HTTP/HTTPS | Фактичні атрибути відповідають таблиці вище | `Lab03CookieProfileTests` - 4 passed |
| Rate limit | Quota, 429, recovery, independent host | 5/60, `429`, recovery через 61 с; GET не обмежено | `Lab03RateLimitAndEventsTests.LoginQuota…` |
| Browser flow | Register → login → profile → create → logout | `201 → 204 → 200 → 201 → 204`, після logout `/api/me` `401` | Знімки нижче; фактичний UI flow |

Браузерний flow виконано на окремому синтетичному акаунті
`browser-lab03-1791998203141`; Alice/Bob перевірено інтеграційними сценаріями.
П'ять спостережень: до входу profile відсутній; після входу видно активний
сеанс і лише safe profile fields; create має тільки title/description/severity/
occurredAtUtc та дає `201`; після logout profile і результат create прибрані,
форма вимкнена, me повернув `401`; password inputs порожні, записів Console
для flow немає, у коді немає запису credentials/principal у browser storage.
Storage не перевірено через runtime-інспектор: доказ відсутності запису -
огляд власного клієнтського коду. Значення cookie і password payload не знімалися.

![Активний сеанс, порожні password inputs та створений інцидент](screenshots/lab03/01-active-created.png)

![Після logout: анонімний стан, прибраний profile і вимкнене create](screenshots/lab03/02-logout-anonymous.png)

## 6. Декларація використання ШІ та підготовка до подання

**ШІ використано:** Codex (OpenAI), для аналізу методички й проєкту, підготовки lab03-report.
Рев'ю змін, виправлення структури тестів, поверхневий перегляд проекту та форматування md файлів, верстка для логіну
