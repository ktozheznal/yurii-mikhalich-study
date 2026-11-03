# F-01 Відсутність перевірки antiforgery для cookie mutations

**Статус:** Fixed у перевіреному незакоміченому робочому дереві; Release build пройдено; фінальний прогін - 105 passed, 0 failed, 0 skipped.  
**Актив:** цілісність власних інцидентів, привілейованих переходів статусу та cookie-сеансу.  
**Компонент:** JSON mutation endpoints і logout у SecureLab API.  
**Пріоритет:** P2.

## Передумови і межі огляду

Початок огляду: `cfe2d6bd7664112994684a03c279cd0a1e90e299`. Відтворення 3 листопада 2026 року виконано на окремій локальній копії початкового стану, API `http://localhost:5086`, Development, штучний обліковий запис Alice. БД для ЛР6 ізольована від попередніх робіт, порт `54333`. Клієнт доказу: Chromium і cookie-aware APIRequestContext. Для нового тестового інциденту Alice була власником, тому штатне правило owner-only дозволяло редагування.

Периметр містить `POST /api/incidents`, `PUT /api/incidents/{id}`, `DELETE /api/incidents/{id}`, `POST /api/incidents/{id}/triage`, `POST /api/auth/logout`. Публічні login/register не спираються на вже видану authentication cookie; їхній окремий login-CSRF/account-creation ризик враховано як межу огляду, а не як автоматично захищений сценарій.

## Відтворення

1. Підготувати локальний seed у Development і ввійти як Alice штатним login; значення пароля та cookie зберігати поза доказом.
2. Створити власний інцидент і прочитати його. Фактичний початковий description: `Початковий навчальний опис`.
3. У тій самій cookie-сесії надіслати коректний `PUT /api/incidents/{id}` з новим description, без `X-CSRF-TOKEN`.
4. Очікувана властивість: сервер відмовляє й ресурс не змінюється. Фактичний початковий результат: `204`, після читання description дорівнює `Зміна без CSRF токена`; `stateUnchanged=false`.

## Вплив і ймовірність

**Статус перевірки:** відсутність серверної перевірки та зміна ресурсу спостережені локально; повний довільний cross-site browser exploit не заявляється.

Authentication cookie сама не доводить намір користувача виконати mutation. Якщо браузер надішле чинну cookie з небажаним запитом, початковий API не вимагав додаткового antiforgery proof. Це стосується також завершення сеансу та привілейованої зміни статусу за наявності відповідних прав. SameSite=Lax, JSON Content-Type та точний CORS звужують доступні способи доставки; умови same-site і cross-origin відрізняються. Тому за результатом одного локального cookie-запиту не робиться висновок про обхід усіх цих browser controls. P2 обрано через можливий вплив на стан і наявні додаткові обмеження доставки.

## Першопричина

Початкові handlers перевіряли authentication, DTO і права на ресурс, але не викликали `IAntiforgery.ValidateRequestAsync`. JSON minimal APIs не отримують потрібної перевірки лише від cookie authentication або CORS. Logout викликав `SignOutAsync` без перевірки request token.

## Виправлення

У `src/SecureLab.Api/Scaffolding/AntiforgeryProtection.cs` створено стандартну реєстрацію ASP.NET Core antiforgery, authenticated `GET /api/antiforgery/token` і спільний endpoint filter. Контракт заголовка: `X-CSRF-TOKEN`; відповідь видачі має `Cache-Control: no-store`. Cookie `SecureLab.Antiforgery` має HttpOnly, SameSite=Strict, Secure=SameAsRequest у Development і Always у Production.

Фільтр перевіряє валідну пару перед handler і перетворює `AntiforgeryValidationException` на точний `400 ProblemDetails` з title `Перевірку CSRF відхилено`, без вмісту токена або поля `errors`. Він підключений у `Lab02Endpoints.cs:156`, `IncidentMutationEndpoints.cs:35,45,67` та `Lab03Identity.cs:52`. Прямий виклик валідації міститься в `AntiforgeryProtection.cs:41`. GET endpoints не охоплено mutation filter.

Клієнт використовує спільний helper, одержує token перед першою mutation на поточній сторінці та очищає його після успішного login/logout. Це підтримує перехід між сторінками й F5 та не зберігає token у browser storage.

Виправлений commit не створено за прямою вказівкою користувача. Об’єкт повторної перевірки - незакомічене робоче дерево; точний фінальний склад фіксує manifest доказів.

## Retest

Повторення в тому самому профілі Development і local HTTP, Chromium та cookie-aware APIRequestContext, виконано 3 листопада 2026 року на fixed API `localhost:5080`. Фактичний доказ `docs/lab06-evidence/fixed.json`, `REG-CSRF-01`, resource `efa4ad67-533a-4654-ab8b-28dd498349c4`: без request token отримано `400`, повторний GET зберіг description `Початковий навчальний опис` (`stateUnchanged=true`); невалідний token також дав `400`. Після одержання штатної пари той самий PUT дав `204`, description став `Легітимна зміна з валідною парою CSRF`. Отже початковий сценарій з `docs/lab06-evidence/before.json` повторено на реальному локальному HTTP API і відхилено.

Власний `REG-CSRF-01` повторює той самий коректний PUT без header: потрібні точний `400` і незмінний description після повторного GET. Окремі сценарії поширюють перевірку відсутнього та невалідного token на всі п’ять mutation routes. Перевірка logout додатково вимагає, щоб після відмови `GET /api/me` залишався `200`.

Автоматизований test: `Lab06HardeningTests.REG_CSRF_AllCookieMutations_RejectWithoutValidToken_KeepState_ThenAcceptValidPair`. Фактичний фінальний прогін `TestResults/lab06-final.trx`: усі 10 варіантів (п’ять операцій, відсутній або невалідний token) пройшли. Для кожного отримано `400` і перевірено незмінний стан, після валідної пари - штатний success і саме очікувану зміну. Повторний вхід іншого користувача перевіряє `REG_CSRF_TokenIsBoundToAuthenticatedIdentity_AndFreshPairWorksAfterNewLogin`: Passed, token попереднього principal відхилено, свіжа пара прийнята. Загальний набір: 105 passed, 0 failed, 0 skipped.

## Positive regression

У тому самому cookie-сеансі з token і `X-CSRF-TOKEN` фактично підтверджено create `201`, update/delete/logout `204`, triage `200`. Перевірка після відповіді довела відповідно новий запис, змінений description, відсутній видалений запис, `New → Triaged` і history+1 або `401` після logout. Старі validation/authorization тести одержують реальну пару токенів, щоб CSRF-відмова не маскувала початкову перевірку; вони також пройшли у повному наборі.

Окремий browser retest у `fixed.json`: після переходу й F5 форма створення дала `201` (`REG-UI-F5`); введений HTML лишився literal text, створених HTML elements - `0`. Штатний logout дав `204`, наступна перевірка сеансу - `401` (`REG-UI-LOGOUT`).

## Залишковий ризик

Antiforgery не замінює authentication, resource authorization або safe DOM і не захищає від коду, який уже виконується в дозволеному origin. Login/register залишаються публічними й мають окремий прийнятий ризик у межах локального стенда. Production protected flow потребує HTTPS; HTTP Production використовується лише для контрольованої діагностики 500. За межами одного instance потрібен узгоджений Data Protection key ring.

## Доказ

Початкове спостереження: `docs/lab06-evidence/before.json`, ID `REG-CSRF-01`, resource `890ebf6a-50d7-4d99-9238-cb06f75edc1f`, `204`, description змінено без request token. Фінальний парний доказ: `docs/lab06-evidence/fixed.json`, той самий ID сценарію, `400`, незмінний стан, потім `204` і легітимна зміна. До доказів не включено password, authentication cookie або request token.

Команда повторення регресії після штатної підготовки ізольованої БД:

```powershell
dotnet test tests/SecureLab.Api.Tests -c Release --filter FullyQualifiedName~Lab06HardeningTests.REG_CSRF
```

Стандартний механізм звірено з [офіційною документацією ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).
