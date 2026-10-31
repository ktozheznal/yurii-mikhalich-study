# Докази безпеки браузера ЛР 5

Сценарій 2-A, рівень «Добре». Перевірки виконуються лише на власному локальному стенді зі штучними даними. Значення passwords, cookies, tokens і browser storage не входять до доказів.

## Стенд і стани

| Параметр | Значення |
|---|---|
| Дата | 31 жовтня 2026 року |
| ОС і браузер негативного контролю | Windows, Chrome `156.0.8078.49` |
| API та штатний клієнт | `http://localhost:5080` |
| Allowed probe | `http://localhost:4173` |
| Disallowed probe | `http://localhost:4174` |
| БД для перевірок | Окремий Compose project `securelab-lab5-verification`, порт `54332` |
| Scaffold | `lab-05-start-v1`, чотири навчальні файли; marker скопійовано з однорядкового файла |
| Git на початку роботи | HEAD `419d5aca741b89eae99e32910c72479e39c4de64`, наявна гілка `lab5` |
| Git дії | Нових commits, tag `v0.5.0`, merge та push не виконано за вказівкою користувача |
| Ідентифікація до/після | Маніфести SHA-256 вихідних файлів; вони не замінюють commit hashes DEL-02 |

Негативний контроль збережено у [before.json](lab05-evidence/before.json). [Safe retest без CSP](lab05-evidence/safe.json) пройшов 6 сценаріїв: Description, контроль Bob, public comment та три позитивні рядки. [Остаточний стан із CORS/CSP](lab05-evidence/final.json) пройшов 3 XSS-контролі, 9 CORS-перевірок, перевірку трьох document responses та CSP enforcement. [Контроль без AllowCredentials](lab05-evidence/nocredentials.json) пройшов 8 CORS cases; після нього AllowCredentials повернуто. JSON містить actual DOM і мережеві події; це результати виконаних browser перевірок, а не лише перевірка вихідного коду.

Фактично прочитані browser cookie metadata з `final.json`: Name `.AspNetCore.Identity.Application`, Domain `localhost`, Path `/`, HttpOnly `true`, SameSite `Lax`, Secure `false`, session cookie (`expires: -1`). Value не зберігалося. Це узгоджується з HTTP Development і `SameAsRequest`; поза Development конфігурація вимагає Secure. Різні порти localhost є різними origins, але тим самим site; цей дослід не доводить cross-site поведінку SameSite.

## Гіпотеза та негативний контроль XSS

[Карта обох flow](source-to-sink.md) простежує користувацький Description і seed-коментар до DOM. Гіпотеза: значення безпечне як JSON/DB text стає виконуваним, коли `innerHTML` передає його HTML parser. Один marker застосовано в обох полях.

| Спостереження до fix без CSP | Фактичний результат | Доказ |
|---|---|---|
| Description інциденту `672816db-e333-410a-9834-dd0a68ab8d3b`, Morgan | `GET: 200`; створено `img`; marker `executed` | `before.json`, `xss[field=description,user=morgan]`; [зображення](lab05-evidence/before-description-morgan.png) |
| Той самий інцидент, Bob | `GET: 403`; DOM порожній; marker `null` | `before.json`, `xss[field=description,user=bob]`; [зображення](lab05-evidence/before-description-bob.png) |
| Seed-коментар `30000000-0000-0000-0000-000000000005`, Morgan | `GET: 200`; `#comments` містить `p > img`; marker `executed` | `before.json`, `xss[field=comment]`; [зображення](lab05-evidence/before-comment-morgan.png) |
| Мережевий ефект marker | Власний `GET http://localhost:5080/x`; зовнішніх hosts немає | `before.json`, Network request типу Image |

Початкова дія Alice створювала marker-інцидент через штатний protected POST; stored характер доводить подальший GET в окремому сеансі Morgan. Успадкована заборона Bob лишилася чинною. Marker не читає credentials і не надсилає дані назовні.

Причина й fix: `browser-lab.js` розбирав Description і texts comments як HTML. Тепер єдиний `renderText(target, value)` записує `target.textContent = value ?? ""`; коментарі створюються через `createElement("p")`, контейнер очищується `replaceChildren()`. Це зміна sink, а не blacklist або зміна збереженого рядка.

| Retest після fix | Фактичний результат | Доказ |
|---|---|---|
| Той самий Description у Morgan | `GET: 200`, marker буквально в textContent, images `0`, marker `null`; Image request `x` відсутній | [safe.json](lab05-evidence/safe.json), [final.json](lab05-evidence/final.json), [final DOM](lab05-evidence/final-description-morgan.png) |
| Той самий public comment | Marker буквально в тексті `p`, images `0`, marker `null` | `safe.json`, `final.json`; [final DOM](lab05-evidence/final-comment-morgan.png) |
| Bob | `GET: 403`, marker `null`, images `0` | `safe.json`, `final.json` |
| Лапки, апостроф, `5 < 7`, `</>`, Unicode | `roundTripEqual:true`, вкладених HTML elements `0`; значення довжиною 43 і 22 | `safe.json`, cases `legitimate` |
| Description довжиною 4000 | Створено й прочитано без зміни; `roundTripEqual:true`, HTML elements `0` | `safe.json`, case `legitimate`, `inputLength:4000` |
| DB/API значення після fix | Обидва збережені marker дорівнюють початковому рядку, що до fix створював `img` | [дві DB строки](lab05-evidence/db-marker-after-safe.txt), `safe.json` і [вхід](lab05-marker.txt) |

Перший retest safe DOM фактично виконано без CSP (`safe.json`): відсутність marker effect під enforcement сама по собі не довела б виправлення sink. Тут доказом є буквальний текст і відсутність `img` ще до додавання CSP.

## REG-XSS-01

Передумови: перевірена локальна БД ЛР5, Chrome/Edge, canonical marker [lab05-marker.txt](lab05-marker.txt), Alice та Morgan з різними cookie stores. Не скидайте БД попередніх лабораторних робіт. Seed password задайте в середовищі поза Git; його не записуйте у звіт, HTTP-файли або командний рядок.

1. З кореня проєкту запустіть `./scripts/verify-lab05.ps1 -Rounds 1 -Serve`. Скрипт піднімає окрему БД, виконує reset/tests і запускає API на 5080. Для повного повторення набору використайте `-Rounds 2`. Якщо seed password не задано, скрипт генерує його в пам'яті; для входу seed accounts задайте власний password заздалегідь.
2. Увійдіть як Alice на `/auth-lab.html`, переконайтеся в Login 204 і `/api/me` 200. Відкрийте `/browser-lab.html`, DevTools Network: Preserve log, Disable cache. Console: `document.querySelector('#lab05-marker-target').getAttribute('data-lab05-marker')` має повернути `null`.
3. Уведіть унікальний Title, вставте єдиний рядок marker в Description, натисніть «Створити й прочитати». Перевірте POST 201 → GET 200, скопіюйте GUID, звірте response `description` із marker.
4. У fixed state Description має показати сам рядок. У Elements `#description` містить текст, не `img`; Console повертає `null`; Image request `/x` від цього render відсутній. Повторне читання не має змінювати результат.
5. В окремому браузерному профілі або приватному вікні увійдіть як Morgan, відкрийте той самий GUID: GET 200 і ті самі safe DOM результати. В окремому сеансі Bob: GET 403, marker `null`.
6. Після нового завантаження сторінки прочитайте seed incident `20000000-0000-0000-0000-000000000001` як Alice або Morgan. Public comment `...0005` має показувати marker буквально, без `img`, marker `null`. Morgan також може бачити internal comment ЛР4, Alice його не бачить.
7. Повторіть створення для `Апостроф ' лапки " 5 < 7 та </> Україна 🛡️`, `<b>звичайний текст</b>` і 4000 символів `я`, щоразу з новим Title. Перевірте POST 201, GET 200, точну рівність рядка, відсутність HTML elements. Для 4001 символу очікується 400 validation, а не обрізання тексту.
8. Звірте DB/API marker з початковим значенням; safe rendering не повинен переписувати `description` або comment `text`. Збережіть лише потрібні response/DOM observations без cookie/password.

Негативний контроль вже збережено у `before.json`: на виданому unsafe scaffold без CSP Morgan отримував `img` і marker `executed` для обох полів; Bob отримував 403. Для повторення негативного контролю використовуйте окрему копію vulnerable snapshot і локальну БД, не повертаючи unsafe rendering у фінальні файли. Не створюйте Git commit лише для цієї процедури.

## Прогноз і CORS до policy

| Сценарій | Чи доходить request до API | Чи читає JS response без CORS policy | Причина |
|---|---|---|---|
| Same-origin 5080 → API | Так | Так, у межах authz | CORS не потрібен |
| Simple GET 4173 або 4174 → API | Так | Ні | SOP/CORS приховує response |
| GET із X-Lab-Cors-Probe | Спочатку OPTIONS, actual GET залежить від дозволу | Ні | Preflight не отримує дозвіл |
| Direct HTTP / curl | Так | Browser JS не застосовується | Клієнт не застосовує SOP |

Підтверджено до policy: всі 8 browser cases у `before.json` (simple, preflight, cookie, anonymous для 4173/4174) дали `readable:false`, `TypeError`. Це правильний негативний контроль; HTTP/network причина наведена в масиві `network` кожного case.

## REG-CORS-01

API має працювати на 5080. У двох окремих терміналах із кореня проєкту:

```powershell
py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe
```

```powershell
py -3 -m http.server 4174 --bind localhost --directory demonstrations/cors-probe
```

Відкрийте обидва probes в браузері. Network: Preserve log, Disable cache. Для preflight за потреби використайте новий query `?probe=2`, щоб не сплутати його з окремим preflight cache. Не змінюйте ім'я дозволеного header.

1. На сторінці 4173 виконайте в Console `await fetch('http://localhost:5080/health', {credentials:'omit'}).then(async r => ({status:r.status, body:await r.text()}))`. Очікується 200 і читабельне тіло. Повторіть на 4174: TypeError/CORS error, хоча actual GET доходить до API.
2. На обох origins виконайте `await fetch('http://localhost:5080/health?probe=2', {credentials:'omit', headers:{'X-Lab-Cors-Probe':'stage-3'}}).then(async r => ({status:r.status, body:await r.text()}))`. Для 4173 збережіть OPTIONS 204 → GET 200; OPTIONS request містить Origin, Access-Control-Request-Method GET, Access-Control-Request-Headers x-lab-cors-probe. Відповідь дозволяє точний origin, GET і header. Для 4174 OPTIONS не має Allow-*; actual GET не надсилається. Сам рядок помилки GET у Network не доводить його надсилання.
3. У тому самому cookie store увійдіть як Alice або Morgan на 5080. На 4173 встановіть probe API URL `http://localhost:5080/api/incidents` і натисніть GET (кнопка використовує credentials Include). Очікується читабельний 200, ACAO `http://localhost:4173`, ACAC `true`. На 4174 actual GET може мати HTTP 200, але браузер не надає response JavaScript. Cookie Value не зберігайте.
4. Еквівалентний cookie-дослід можна виконати для protected `/api/me`; цей endpoint застосовано в автоматизованому записі. На відміну від `/health`, він підтверджує auth cookie, оскільки без cookie повертає 401.
5. Короткий негативний контроль: приберіть тільки `.AllowCredentials()` із Development policy, перезапустіть API, повторіть Include із 4173. Очікується CORS відмова через відсутність ACAC true. Поверніть `.AllowCredentials()` і перезапустіть API; фінальний стан має лишитися з ним.
6. У приватному вікні без входу на 4173 прочитайте `/api/me` з credentials Include: JS має прочитати HTTP 401 з ACAO/ACAC. Це доводить правильний порядок UseCors до authorization, а не успішний login.
7. Порівняйте direct HTTP без credentials. Обидві команди повертають 200, але лише allowed response містить ACAO/ACAC:

```powershell
curl.exe -sS -D - -o NUL -H "Origin: http://localhost:4173" "http://localhost:5080/health"
curl.exe -sS -D - -o NUL -H "Origin: http://localhost:4174" "http://localhost:5080/health"
```

Зберігайте фактичні response headers обох відповідей. Для denied origin 200 у curl очікуваний: CORS не є серверною забороною запиту.

| Fixed case | Фактичний результат | Файл retest |
|---|---|---|
| 4173 simple | GET 200, `readable:true`, ACAO `http://localhost:4173` | `final.json`, case simple |
| 4173 preflight | OPTIONS 204 → GET 200, Allow-Origin `4173`, Methods GET, Headers X-Lab-Cors-Probe | `final.json`, case preflight |
| 4174 simple | GET request зафіксовано, response JS не надано; `MissingAllowOriginHeader` | `final.json`, case simple |
| 4174 preflight | OPTIONS 204 без Allow-*; `PreflightMissingAllowOriginHeader`, actual response GET відсутня | `final.json`, case preflight |
| 4173 cookie | `/api/me` 200 читабельний, Analyst Morgan; ACAC `true` | `final.json`, case cookie |
| 4174 cookie | `readable:false`, `MissingAllowOriginHeader` | `final.json`, case cookie |
| 4173 без cookie, credentials Omit | Читабельний HTTP 401, ACAO/ACAC збережено | `final.json`, case anonymous |
| 4173 окремий приватний context, credentials Include | Читабельний HTTP 401 | `final.json`, case private-context-include |
| 4173 Include без AllowCredentials | `readable:false`, TypeError; simple/preflight з Omit продовжували читати 200 | `nocredentials.json` |

Остання перевірка в окремому приватному browser context доповнює Omit-контроль: вона підтверджує саме credentials Include без збереженої auth cookie. Після короткого тесту без ACAC фінальний Program.cs знову містить AllowCredentials. Direct HTTP headers обох Origin збережені окремо в [direct-http.txt](lab05-evidence/direct-http.txt).

Policy у `Program.cs`: лише Development; exact `http://localhost:4173`; methods GET; header X-Lab-Cors-Probe; AllowCredentials. `UseRouting` → `UseCors` → authentication → authorization. Wildcard, відбиття довільного Origin і Production дозвіл probe не застосовуються.

## CSP і browser headers

| Директива | Фактична потреба основного клієнта | Обране правило та причина |
|---|---|---|
| default-src | Ресурси одного застосунку | `'self'`, мінімальний fallback |
| script-src | Локальні app.js, auth-lab.js, browser-lab.js | `'self'`, без inline handler та стороннього JS |
| style-src | Локальні styles.css та browser-lab.css, без inline стилів | `'self'` |
| connect-src | fetch до same-origin API | `'self'`; probes не є постійним frontend |
| img-src | Власні images, якщо вони з'являться; marker image не потрібне | `'self'`; контроль до 4173 має блокуватися |
| object-src | Вбудовані plugin objects не потрібні | `'none'` |
| base-uri | Власна база URL | `'self'` |
| frame-ancestors | Вбудовування трекера не потрібне | `'none'`, захист від стороннього embedding |

Обрана enforcement policy:

```text
default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'
```

Вона додається в наявній response-header middleware. Точний виняток `/scalar` діє тільки в Development: сторонній довідник має власні inline/CDN ресурси й не належить до основного flow; через нього policy трекера не послаблюється.

| Контроль | Обране значення | Ризик і обґрунтування | Межа |
|---|---|---|---|
| X-Content-Type-Options | `nosniff` | Зменшує небажане MIME sniffing | Не замінює правильний Content-Type |
| Referrer-Policy | `no-referrer` | URL інцидентів не передається як Referer; клієнт не потребує referrer для навігації | Не дозволяє зберігати secrets в URL |
| CSP frame-ancestors | `'none'` | Трекер не має сценарію embedding; зменшує clickjacking | Не замінює access checks |

Фактично отримано `/`, `/auth-lab.html`, `/browser-lab.html` зі статусом 200; кожна відповідь містить один enforcement CSP header, `nosniff` і `no-referrer` ([final.json](lab05-evidence/final.json), `headers`). Робочі authenticated create/read та повторне читання показані XSS-регресією. Кероване `new Image().src='http://localhost:4173/x.png'` дало actual `securitypolicyviolation`: directive `img-src`, disposition `enforce`, blockedURI `http://localhost:4173/x.png`; Console завершила повідомлення словами `The action has been blocked.` (`final.json`, `csp`). Це окреме підтвердження defense in depth після доведеного safe sink.

## Review XSS CORS CSRF та authorization

XSS у дослідженому flow виникав, коли збережений текст ставав HTML у браузері Morgan. Safe DOM змінює інтерпретацію; CSP додатково обмежує виконання й ресурси. CORS визначає, чи може JS probe прочитати cross-origin response. Denied simple request усе одно може бути виконаний, тому CORS не є CSRF-захистом. Cookie браузер надсилає за правилами cookie/site; state-changing cookie endpoints потребують окремого antiforgery механізму наступної ЛР. Authorization перевіряє права конкретного principal на resource: Bob отримує 403 незалежно від CORS або DOM. Успішний preflight не дає права читати чужий інцидент.

Залишкові межі: лише локальний HTTP Development стенд; cross-site cookie profile не перевірявся; CSRF реалізація не входить до ЛР5; Development Scalar виняток не є послабленням основного CSP. Git-комплект DEL-02 без commits/tag/main лишається незавершеним за прямою вказівкою користувача. Підтверджувати оцінку остаточно може викладач.
