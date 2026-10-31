# Звіт з лабораторної роботи 5

Безпека браузера XSS DOM і межі CORS

Ященко Микола, КБ-41. Рівень Добре. Київ 2026.



## 1 Мета та результат роботи

Мета роботи - простежити недовірені значення від форми або seed до DOM, локально відтворити нешкідливий stored XSS, усунути небезпечне відображення та перевірити межі CORS і browser security headers у власному трекері інцидентів SecureLab.

Виконано технічний обсяг рівня «Добре» для спільного сценарію 2-A: досліджено Incident.Description та IncidentComment.Text, застосовано спільний safe helper, точний CORS allowlist із cookie credentials, базову CSP в enforcement і пов’язані заголовки. Серверні правила authentication та authorization попередньої ЛР збережено.

### Початковий стан і середовище

| Параметр | Фактичне значення |
| --- | --- |
| Дата перевірки | 31 жовтня 2026 року |
| Проєкт | yurii-mikhalich-study; власний завершений стан ЛР 4 |
| Baseline та рівень | 2-A; Добре |
| Гілка | lab5; робочий стан без нових комітів |
| Середовище | Windows; .NET SDK 10.0.303; Chrome 156.0.8078.49 |
| Окрема БД | PostgreSQL у Compose securelab-lab5-verification; 127.0.0.1:54332 |
| API та probes | http://localhost:5080; http://localhost:4173; http://localhost:4174 |
| Scaffold | lab-05-start-v1; SHA-256 виданих чотирьох файлів перевірено |
| Перевірка | Два повні прогони після reset окремої БД: по 85 passed, 0 failed, 0 skipped; Release build без помилок і попереджень. |
| Версійний контроль | Коміти, нові теги, merge та push не виконувалися за вказівкою користувача |

Чотири нові файли scaffold перенесено пофайлово після перевірки їхніх хешів. Штатний installer не запускався через наявні незакомічені зміни. Vulnerable і fixed стани ідентифіковано маніфестами вихідних файлів; ці ідентифікатори не є commit hashes.



## 2 Карта source to sink

Обидва поля за предметним контрактом є звичайним текстом. База даних і JSON не надають рядку довіри: значення залишається введенням іншого користувача, навіть коли його отримано через захищений endpoint.

### Основний потік Incident Description

| Етап | Файл або дія та результат |
| --- | --- |
| Source | Форма #edit; textarea name=description на browser-lab.html |
| Request і validation | browser-lab.js: POST /api/incidents; JSON.stringify. Lab02Endpoints: nonempty, <=4000 символів, Trim; для High/Critical не менше 40 |
| Storage | incidents.description через EF Core; plain text без HTML sanitizer |
| Response | GET /api/incidents/{id}; IncidentQueries повертає description після read authorization |
| Transformations | response.json(); current.description; JSON decoding відновлює початкові символи |
| Unsafe sink | Було: #description.innerHTML = current.description; HTML parser створював img з onerror |
| Fixed sink | renderText(#description, current.description); helper використовує textContent |

### Другий потік IncidentComment Text

| Етап | Файл або дія та результат |
| --- | --- |
| Source | Локальний DbSeeder; публічний коментар Morgan до AliceIncidentId |
| Storage | incident_comments.text; межа схеми 2000 символів; IsInternal=false |
| Response | Той самий details GET, comments[].text. Parent і internal visibility перевіряються сервером |
| Unsafe sink | Для кожного comment створювався p, потім paragraph.innerHTML = comment.text |
| Fixed sink | createElement("p") + renderText(paragraph, comment.text) + append |

Форма коментарів і comment-create API у цьому scope відсутні. Seed є відтворюваним джерелом другого поля; наявні коментарі не замінено. Повна карта з назвами файлів збережена в docs/source-to-sink.md.



## 3 Відтворення локального stored XSS

Початковий дослід виконано до ввімкнення CSP. Alice створила інцидент із канонічним marker із docs/lab05-marker.txt. В окремому browser context Morgan прочитав збережений об’єкт. Marker встановлює лише локальний атрибут data-lab05-marker="executed" на #lab05-marker-target; credentials не читаються й дані назовні не передаються.

```text
<img src=x onerror="document.querySelector(
  '#lab05-marker-target').setAttribute(
  'data-lab05-marker','executed')">
```

Наведений вище вигляд перенесено для читання. Для точного повторення використовується однорядковий файл marker без переносів.

| Сценарій | HTTP | Фактичний DOM до fix |
| --- | --- | --- |
| Morgan читає Description | 200 | img: 1; marker: executed |
| Bob читає той самий UUID | 403 | Дані приховані; marker: null |
| Morgan читає marker-коментар | 200 | img: 1; marker: executed |

![Рисунок 1 - Початковий render опису у browser client Morgan](lab05-evidence/before-description-morgan.png)

UUID створеного інциденту: 672816db-e333-410a-9834-dd0a68ab8d3b. Коментар: 30000000-0000-0000-0000-000000000005; батьківський AliceIncidentId; автор Morgan; 2026-08-01 09:35 UTC; IsInternal=false.

### Першопричина

Недовірений plain-text рядок потрапляв у HTML parser через innerHTML. Обробник onerror виконувався під час невдалого завантаження локального src=x. Успішна авторизація Morgan дозволяла читання ресурсу, проте не робила його текст безпечним HTML.



## 4 Виправлення та позитивна регресія

Структура DOM створюється програмно. Єдиний helper передає обидва значення в textContent, тому браузер створює текстові вузли замість розбору HTML. Sanitizer не додавався: formatted HTML не є вимогою продукту й заборонений у спільному scope.

```text
function renderText(target, value) {
  target.textContent = value ?? "";
}

renderText(document.querySelector("#description"),
           current.description);
const paragraph = document.createElement("p");
renderText(paragraph, comment.text);
```

![Рисунок 2 - Той самий marker після safe DOM fix до ввімкнення CSP](lab05-evidence/safe-description-morgan.png)

| Перевірка | Фактичний результат |
| --- | --- |
| Той самий Description та коментар | Literal text; img: 0; marker: null; немає render-запиту /x |
| Значення у storage та API | Marker не замінено й не очищено; змінено тільки DOM sink |
| Апостроф, лапки, 5 < 7 та </> | Пройшли POST/GET і відобразилися буквально |
| HTML-like <b>звичайний текст</b> | Показано як plain text; елемент b не створено |
| Український Unicode | Відображення й API round trip без втрати символів |
| Description довжиною 4000 | Прийнято; довжина й текст збережені |

Retest зроблено окремо від CSP: ще без policy небезпечний елемент не створюється. Після додавання CSP цей DOM-критерій повторено. Самої відсутності виконання marker недостатньо, якщо в DOM залишився img з inline handler.



## 5 Origin і точний CORS allowlist

Origin визначається схемою, host і port. API на localhost:5080 та probes на localhost:4173 і localhost:4174 мають різні origins. Стенд навмисно використовує один host localhost; порти не входять до поняття cookie site.

У Development зареєстровано одну іменовану policy BrowserLabProbe. Middleware UseCors розташована після routing і до authentication/authorization, тому дозволений origin отримує CORS headers навіть для відповіді 401. У Production навчальна cross-origin policy не додається.

```text
.WithOrigins("http://localhost:4173")
.WithMethods("GET")
.WithHeaders("X-Lab-Cors-Probe")
.AllowCredentials()
```

| Дослід | 4173 | 4174 |
| --- | --- | --- |
| До policy: simple GET /health | Fetch TypeError; response недоступний JS | Fetch TypeError; response недоступний JS |
| Після policy: simple GET /health | Readable 200; JSON ready | GET виконався; response приховано |
| Після policy: GET з custom header | Preflight дозволений; actual GET 200 | Preflight без allow headers; actual GET відсутній |
| Direct HTTP /health із Origin | 200; HTTP-клієнт читає body | 200; HTTP-клієнт читає body |

### Що доводить простий заборонений GET

У Network видно, що простий запит із недозволеного origin доходить до сервера. Браузер приховує відповідь від JavaScript через відсутність Access-Control-Allow-Origin. Тому CORS не є firewall і не гарантує, що запит узагалі не виконається.

### Чому direct HTTP читає відповідь

curl та інші HTTP-клієнти не застосовують browser SOP/CORS. Сервер повертає HTTP response незалежно від того, чи клієнт уміє застосувати browser policy. Право на protected ресурс усе одно перевіряється через cookie та серверну authorization.

Докази: browser-security-evidence.md, before.json, final.json та direct-http.txt. У записаних network headers вилучено cookie й authorization.



## 6 Preflight та cookie credentials

Custom header X-Lab-Cors-Probe спричиняє browser OPTIONS перед actual GET. Це автоматичний browser protocol step, а не ручна імітація. У доказах збережено Origin, Access-Control-Request-Method/Headers і відповідні allow headers.

| Заголовок | Дозволений preflight 4173 |
| --- | --- |
| Request Origin | http://localhost:4173 |
| Access-Control-Request-Method | GET |
| Access-Control-Request-Headers | x-lab-cors-probe |
| Response status | 204 |
| Access-Control-Allow-Origin | http://localhost:4173 |
| Access-Control-Allow-Methods | GET |
| Access-Control-Allow-Headers | X-Lab-Cors-Probe |
| Access-Control-Allow-Credentials | true |

Для 4174 OPTIONS також повернув 204, проте без потрібних allow headers. Browser не надіслав actual GET із custom header. Сам status 204 не означає дозволу CORS: браузер зіставляє заголовки з origin, методом і запланованими headers.

### Дослід cookie-профілю

| Передумова й дія | Спостереження |
| --- | --- |
| Morgan увійшов; 4173; credentials include | GET /api/me: readable 200; конкретний allow origin і allow credentials true |
| Morgan увійшов; 4174; credentials include | Запит виконується, але JavaScript не читає response |
| 4173; credentials omit або приватний сеанс без входу з include | Readable 401 в обох контролях; CORS не надає особу |
| Негативний контроль без AllowCredentials | 4173 credentials include заблоковано для JS; policy потім відновлено |

Cookie налаштовано HttpOnly, SameSite=Lax; Development HTTP-профіль SameAsRequest успадковано від ЛР3. Тут різні порти є cross-origin, але same-site. Це не доказ працездатності cookie між довільними різними sites. Wildcard origin із credentials не використовується.



## 7 CSP і browser security headers

У наявній middleware заголовків додано Content-Security-Policy в enforcement. Основний клієнт, сторінка входу та browser-lab.html працюють із зовнішніми скриптами й стилями того самого origin. Unsafe-inline та wildcard не додавалися.

```text
default-src 'self'; script-src 'self'; style-src 'self';
img-src 'self'; connect-src 'self'; object-src 'none';
base-uri 'self'; frame-ancestors 'none'
```

| Ресурс або контроль | Фактично перевірено |
| --- | --- |
| / | Документ 200; одна enforcement CSP; nosniff; no-referrer |
| /auth-lab.html | Документ 200; same-origin зовнішній auth-lab.js |
| /browser-lab.html | Документ 200; same-origin browser-lab.js; safe DOM працює |
| new Image() до localhost:4173/x.png | securitypolicyviolation; img-src; disposition=enforce; дію заблоковано |
| Development /scalar | Точний виняток для API reference із власними inline/CDN ресурсами |

### Обґрунтування policy

| Header або директива | Ризик і рішення |
| --- | --- |
| X-Content-Type-Options: nosniff | Зменшує MIME confusion. Не замінює правильний Content-Type чи validation |
| Referrer-Policy: no-referrer | Не передає URL сторінки як referrer. Клієнт не потребує referrer для переходів, тому це прийнятно |
| frame-ancestors 'none' | Клієнт не має потреби вбудовуватись в iframe; заборона зменшує clickjacking |
| CSP як додатковий шар | Обмежує джерела й inline execution, але не виправляє unsafe DOM parser sink |

Контрольоване порушення CSP виконано після перевірки документів зі status 200. Report-only не заявляється: для обраного рівня остаточний enforcement є достатнім. CSP не використано як заміну safe DOM fix.



## 8 Доказова таблиця виконання

| ID | Сценарій | Фактичний результат | Доказ |
| --- | --- | --- | --- |
| X-01 | Description до fix | Morgan 200; img 1; executed | before.json; рис. 1 |
| X-02 | Відмова Bob | 403; вміст не розкрито | before/safe/final.json |
| X-03 | Comment до fix | Публічний seed; executed | before.json |
| X-04 | Safe rendering обох полів | img 0; literal marker; null | safe.json; рис. 2 |
| X-05 | Незмінність даних | DB/API marker збережено | db-marker-after-safe.txt |
| X-06 | Legitimate text | Складні рядки; 4000 символів | safe.json |
| C-01 | Simple probes до policy | Обидва origins unreadable | before.json |
| C-02 | Simple probes після policy | 4173 читає; 4174 приховано | final.json |
| C-03 | Actual browser preflight | Allowed OPTIONS→GET; denied без GET | final.json |
| C-04 | Cookie include й omit | 4173: 200/401; 4174 blocked | final.json |
| C-05 | Без AllowCredentials | Негативний контроль blocked | nocredentials.json |
| C-06 | Direct HTTP | Обидва Origin: 200 | direct-http.txt |
| H-01 | CSP та headers | Три документи 200; enforcement | final.json |
| H-02 | Контрольований CSP block | img-src; enforce | final.json |

### Review чотирьох різних механізмів

XSS виникав на межі інтерпретації рядка як HTML; виправленням є textContent. CORS визначає, чи прочитає script іншого origin response; він не змінює довіру до самого рядка. CSRF стосується небажаних state-changing дій з автоматичними credentials; CORS не замінює antiforgery або перевірку origin. Authorization вирішує, чи конкретний principal має право на ресурс; Morgan read і Bob 403 перевірені незалежно від XSS.



## 9 Відтворення й регресійні процедури

Перевірки виконуються лише на localhost зі штучними даними. Скрипт використовує окремий Compose project і volume ЛР5; reset не зачіпає звичайну БД та БД попередніх робіт. Пароль seed задається поза Git; скрипт може згенерувати його в пам’яті.

```text
./scripts/verify-lab05.ps1
# Один прогін і запуск перевіреного API:
./scripts/verify-lab05.ps1 -Rounds 1 -Serve
```

У двох окремих терміналах зі встановленим Python 3 запустити probes з каталогу demonstrations/cors-probe:

```text
py -3 -m http.server 4173 --bind 127.0.0.1
py -3 -m http.server 4174 --bind 127.0.0.1
```

### REG XSS 01

Після reset увійти як Alice, відкрити /browser-lab.html та створити новий інцидент з унікальною назвою й однорядковим marker у Description. В окремому сеансі Morgan прочитати UUID. Очікується GET 200, буквальний текст marker, відсутність дочірнього img і marker-атрибуту. Для другого поля прочитати AliceIncidentId та перевірити seed-коментар. Bob для чужого UUID має отримати 403. Повторити legitimate text і Description 4000 символів.

### REG CORS 01

Відкрити обидва probes. Простий fetch /health: 4173 читає 200, 4174 отримує CORS failure. Повторити з X-Lab-Cors-Probe й перевірити actual OPTIONS: лише 4173 отримує allow headers і actual GET. Після входу Morgan додати credentials include до /api/me: 4173 читає 200; без cookie читає 401. Перевірити той самий GET direct HTTP та пояснити різницю.

Негативний контроль без AllowCredentials виконується лише як тимчасовий локальний дослід, після нього точна policy відновлюється. Безпечний кінцевий проєкт не містить конфігураційного перемикача для вмикання unsafe DOM.

### Джерела

Основне джерело вимог - надані «Методичні рекомендації ЛР 05» та scaffold lab-05-start-v1. Візуальний зразок - звіт ЛР4 Ященка Миколи.

Microsoft Learn, Enable Cross-Origin Requests in ASP.NET Core: https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0

MDN, Content-Security-Policy frame-ancestors: https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/frame-ancestors



## 10 Збірка та ідентифікація станів

Два повні прогони після reset окремої БД: по 85 passed, 0 failed, 0 skipped; Release build без помилок і попереджень.

Release build завершено без помилок і попереджень. Інтеграційні перевірки охоплюють попередній authentication/authorization контракт, нові CORS headers, preflight і документи з CSP. Browser evidence одержано у справжньому Chromium; network події записано через Chrome DevTools Protocol, без cookie/token у доказовому комплекті.

### Версійний контроль і межі подання

Комітів, push, merge та нового тегу v0.5.0 не створено відповідно до прямої вказівки користувача. Git-частина DEL-02, яку вимагає методичка, залишається невиконаною. Маніфести SHA-256 і архів джерел ідентифікують перевірені файли, але не підмінюють commit hashes, тег чи Git bundle.

Початковий і поточний Git HEAD: 419d5aca741b89eae99e32910c72479e39c4de64

SHA-256 fixed source manifest: 19626df9e19f2b52738340223b88a56e0c14ea65ebf2999843ad8b78f1ee491b

Vulnerable source manifest і вихідна локальна копія збережені окремо від робочого fixed проєкту. У фінальному browser-lab.js обидва досліджені значення відображаються тільки як plain text.

## 11 Висновок і залишковий ризик

Досліджено два stored flows і підтверджено, що HTML-like значення з БД можуть виконуватися лише через небезпечну клієнтську інтерпретацію. Спільний textContent helper усунув першопричину: той самий marker зберігається у DB/API, проте стає текстом у DOM. CORS обмежує читання cross-origin response точним development origin; CSP та заголовки додають незалежні обмеження браузера.

Залишкові межі: cookie mutation endpoints потребують окремого CSRF-захисту наступної ЛР; зміни в інших майбутніх DOM sinks вимагають повторного review. Дозволені same-origin скрипти залишаються довіреним кодом. CSP не надає автоматичної довіри до їхніх операцій. Політика 403 успадковано розкриває факт існування UUID, але не вміст забороненого інциденту.

### Використання ШІ

Використано OpenAI Codex: аналіз вимог і scaffold, збирання локальних доказів і компонування звіту.


## 12 Відповіді на контрольні питання

### 1 Чому value з БД може залишатися недовіреним

БД зберігає дані користувача й не перетворює їх на довірений код. Небезпека виникає, коли renderer інтерпретує цей рядок у виконуваному контексті.

### 2 Source transformation і sink у виконаному flow

Source - textarea або seed коментаря. Transformations - JSON serialization/decoding, DTO mapping і Trim. Sink до fix - innerHTML; після fix - textContent.

### 3 Stored reflected і DOM based XSS

Stored зберігається та потрапляє до іншого глядача; reflected повертається у поточній відповіді. DOM-based описує небезпечне клієнтське перетворення. Тут збережений рядок виконувався через DOM sink.

### 4 Чому блокування script не усуває XSS

JavaScript може виконуватися через event handlers та інші contexts. Marker використовує img onerror без script tag; blacklist не змінює небезпечного HTML parser sink.

### 5 Коли textContent а коли sanitizer

Для plain text використовують textContent. Sanitizer потрібен лише за обґрунтованої потреби дозволити частину HTML. У цій роботі formatted HTML заборонено.

### 6 Чому sanitizer не пишуть regular expression

HTML parser має вкладені структури, кодування й контексти, які regex blacklist не моделює коректно. Для дозволеного HTML потрібна перевірена спеціалізована реалізація.

### 7 З яких компонентів складається origin

Схема, host і port. http://localhost:4173 та http://localhost:5080 мають різні origins через порт, хоча в цьому стенді залишаються same-site.



## 12 Відповіді на контрольні питання продовження

### 8 Що надсилається в preflight і хто його ініціює

Браузер автоматично надсилає OPTIONS із Origin, Access-Control-Request-Method та за потреби Access-Control-Request-Headers. Тут причиною став custom header X-Lab-Cors-Probe.

### 9 Хто оголошує та застосовує CORS policy

API оголошує правила через Access-Control-Allow-* headers. Браузер перевіряє їх і вирішує, чи надсилати actual non-simple request та чи надавати response JavaScript.

### 10 Чому curl читає response попри CORS error браузера

curl не реалізує browser same-origin policy. Він бачить звичайний HTTP response; browser обмежує доступ script до response згідно з CORS.

### 11 Чому CORS не є authorization та CSRF захистом

CORS не встановлює особу й не перевіряє право principal на ресурс. Простий credentialed request може виконатись навіть коли response приховано. Для state-changing дій потрібен окремий CSRF контроль.

### 12 Чому wildcard origin не поєднують із credentials

Для credentialed response потрібен конкретний дозволений origin та Access-Control-Allow-Credentials: true. Wildcard не задає такого довіреного origin й браузер не дозволяє читання.

### 13 Межа CSP і чому потрібний safe DOM

CSP обмежує виконання й завантаження ресурсів. Вона може заблокувати inline handler, але unsafe DOM усе одно здатний створювати небезпечні елементи. Safe rendering виправляє саму інтерпретацію даних.

### 14 Ризики nosniff Referrer Policy та frame ancestors

nosniff зменшує MIME sniffing; no-referrer не передає URL джерела в Referer; frame-ancestors none забороняє embedding і зменшує clickjacking. Вони не замінюють validation, authorization чи safe DOM.
