# F-02 Втрата browser security headers на неочікуваній помилці

**Статус:** Fixed у перевіреному незакоміченому робочому дереві; Release build пройдено; фінальний прогін - 105 passed, 0 failed, 0 skipped.  
**Актив:** послідовна політика безпеки HTTP responses і browser client.  
**Компонент:** порядок middleware та встановлення response headers у `Program.cs`.  
**Пріоритет:** P3.

## Передумови і межі огляду

Початок огляду: `cfe2d6bd7664112994684a03c279cd0a1e90e299`. На початковій локальній копії перевірено чотири класи звичайних відповідей у Development на `localhost:5086` і контрольований unexpected error у Production на `localhost:5087` з недосяжним локальним DB target `54399`. Production запущено без launch profile, startup migration/reset вимкнено. Запит - публічний login з вигаданими непорожніми credentials, щоб дійти до сховища; реальні секрети не використовуються.

Це HTTP перевірка error contract і headers. Вона не доводить TLS, стан HSTS браузера або роботу захищеної Production cookie-сесії. Стандартні HSTS loopback exclusions збережено.

## Відтворення

1. На Development API перевірити статичну сторінку `GET /` (`200`) та після Alice login - власні details (`200`), `GET /api/incidents?status=Unknown` (`400` із DTO/parameter errors) і відсутній UUID (`404`).
2. Фактичний початковий результат для всіх чотирьох класів: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` та enforcement CSP з `frame-ancestors 'none'` присутні.
3. Окремо запустити Production HTTP з недосяжним локальним DB target і надіслати login до вичерпання квоти limiter.
4. Фактичний результат: `500 application/problem+json`; усі три перелічені security headers відсутні. Body має безпечний загальний title і traceId; stack trace, SQL та connection string у ньому відсутні. Наявний traceId не є предметом finding.

## Вплив і ймовірність

**Статус перевірки:** розбіжність фактично спостережена локально на початковому стані.

Аварійний шлях відповіді не застосовував той самий defense in depth policy, що й звичайні responses. CSP обмежує дозволені джерела й embedding, nosniff - MIME sniffing, no-referrer - передавання referrer. Відсутність headers на JSON ProblemDetails сама по собі не доводить виконання JavaScript або витік секрету; початковий error body залишався безпечним. P3 обрано через обмежений безпосередній вплив і залежність від unexpected-error path.

## Першопричина

У початковому `Program.cs` `UseExceptionHandler()` оточував middleware, яке присвоювало headers до `await next`. Після винятку exception handler очищав попередньо встановлені headers і формував нову відповідь. Тому source-only перевірка наявності присвоювань у коді не підтверджувала захист остаточної `500` response.

## Виправлення

У `Program.cs:59-76` middleware реєструє `Response.OnStarting` до входу в `UseExceptionHandler`. Значення nosniff, no-referrer і поточної enforcement CSP додаються перед фактичним початком остаточної відповіді, включно з error path. Політику CSP не послаблено. Виняток для Scalar залишається лише в Development, як у попередній лабораторній.

`AddProblemDetails` явно прив’язує `traceId` до поточного Activity або request identifier; клієнт не отримує текст внутрішнього exception. Ця явна прив’язка є супровідною зміною: початковий доказ уже містив traceId.

Виправлений commit не створено за прямою вказівкою користувача. Перевіряється незакомічене робоче дерево, яке має власний фінальний manifest.

## Retest

Повторення того самого Production local HTTP сценарію з недосяжним DB target `54399` виконано 3 листопада 2026 року на fixed API `localhost:5088`, без launch profile. Фактичний доказ `docs/lab06-evidence/fixed.json`, `REG-HEADERS-500`: `500 application/problem+json`, `headersPresent=true`; response має `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` та enforcement CSP з `frame-ancestors 'none'`. Безпечний body містить загальний title і traceId `00-0b623ba5466570097cb98a3159ae1863-23d75641ef878742-00`. Це той самий профіль і тип реального HTTP transport, що в `docs/lab06-evidence/before.json`, де всі три headers були відсутні; змінився лише локальний порт API. Для зв’язку з внутрішнім `ExceptionHandlerMiddleware` порівнюється середній 32-символьний сегмент traceId; до публічного доказу включається лише очищений витяг журналу.

Автоматизований test: `Lab06HardeningTests.ProductionUnexpectedError_IsSafeProblemDetailsWithTraceAndHeaders`. Фактичний результат `TestResults/lab06-final.trx`: Passed. У Production HTTPS TestServer з недосяжною локальною БД отримано `500 application/problem+json`, усі три headers присутні; response не містить exception details, SQL, DB target, шляхів чи credentials. Diagnostic capture містить подію exception handler і не містить контрольних credentials. TraceId цього фактичного повторного запиту: `00-81dfdda5d96d9d558f02a0cd9a7c2d0a-ee41be1e08e625f5-00`. Цей TestServer результат відокремлено від початкового Production HTTP transport; він доводить application error path. Загальний набір: 105 passed, 0 failed, 0 skipped.

## Positive regression

Headers на static `200`, успішному API `200`, validation `400` і resource `404` фактично підтверджено зі збереженням коректного status і body. Обидва варіанти `Headers_PersistOnStaticApiValidationAndMissingResource` (Development і Production TestServer hosts) мають Passed у фінальному TRX. HTTPS BaseAddress TestServer перевіряє application/cookie behavior та не є підтвердженням шифрування реального з’єднання.

Таблиця `headers` у `docs/lab06-evidence/fixed.json` окремо підтверджує ці чотири звичайні класи через реальний Development HTTP API `localhost:5080`; для них присутні ті самі nosniff, no-referrer та CSP. Отже виправлення error path зберегло звичайні відповіді.

## Залишковий ризик

Headers доповнюють safe DOM, правильний Content-Type, validation і authorization. Вони не приховують чутливі значення, які розробник сам запише в body або лог, і не замінюють TLS. Фактичний production deployment, certificate chain, reverse proxy та browser HSTS state поза межами локальної роботи. HTTPS redirect вмикається окремо лише після налаштування реального HTTPS listener/proxy.

## Доказ

`docs/lab06-evidence/before.json`, ID `REG-HEADERS-500`: початковий `500`, `headersPresent=false`; таблиця `headers` містить усі п’ять класів відповідей. Початковий traceId: `00-b444de68d1b3295e9b19d04d63965977-51c3278946df84ae-00`. Парний фінальний local HTTP доказ - `docs/lab06-evidence/fixed.json`: `500`, `headersPresent=true`, новий traceId наведено в retest. Докази не містять credentials або token values.

Команда повторення перевірок після штатної підготовки БД:

```powershell
dotnet test tests/SecureLab.Api.Tests -c Release --filter "FullyQualifiedName~Lab06HardeningTests.ProductionUnexpectedError|FullyQualifiedName~Lab06HardeningTests.Headers_Persist"
```

Error pipeline звірено з [офіційною документацією ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0).
