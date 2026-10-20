# Звіт ЛР 4 Авторизація ролі та IDOR BOLA

Рівень: **Добре**. Baseline 2-A. Головна action: details/read. Політика: 403; контракт DTO: A-08a.

Дата: 20.10.2026. Автор: Ященко Микола, КБ-41.

## Початковий стан

HEAD `a4f9fe2ed719c5e79513968245546c11fb3732e6`, гілка `lab4`, власний стан ЛР 3. Комітів, push, merge і нового тегу не виконувалося за вказівкою користувача. Наявні зміни збережено. DEL-02 потребує окремої дії власника.

## Матриця і рішення

[Матриця доступу](access-matrix.md). IncidentAccess централізує Scope/CanRead/CanEdit/CanDelete/CanTriage. Identifier і role лише з verified principal. Regular update копіює тільки title/description; сервер керує owner/status/severity/role. Child route перевіряє parent і internal.

## Finding до причина виправлення після

Alice прочитала Bob details (200) та змінила його description (PUT 204) у контрольованій vulnerable копії. Повторний Bob GET підтвердив зміну. Причина: пошук лише за UUID без ownership. Для mutation у копію додано тимчасовий owner-unchecked PUT; цей код не переносився у робочий проєкт.

Після fixed Alice → Bob details/update/delete дають 403; Bob snapshot без змін. Owner read/update/delete New працюють. Morgan читає details та виконує тільки New → Triaged; повторний triage 403.

Vulnerable source manifest SHA-256: `44520070aed85423658294314cf60e27a91fbfdca4efcde09e7ec5e2e0c0f965`. Fixed source manifest SHA-256: `7042cb3ef7507fe433f24bba9a2c2072d6c58c1e0482a8635542990d475d7643`. Це file fingerprints, **не commit hashes**. Перелік у [маніфесті](lab04-source-manifest.json). Vulnerable копія поза репозиторієм: `../output/lab04/vulnerable-local`.

## Доказова таблиця

| ID | Рівень | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|---|
| A-01 | Добрий | Анонімний доступ | Немає cookie | Protected endpoints | 401 без redirect | 401; HTML redirect немає | A01 theory 8 маршрутів |
| A-02 | Достатній | Власний ресурс | Alice owner | Read/update/delete New | 200/204/204 | 200/204/204; дозволений content | A02; HTTP own 200 |
| A-03 | Достатній | Чужий ресурс | Alice; Bob incident | GET details із чужим id | 403 без даних Bob | 403; немає description/comments | A03; рис. 3 |
| A-03R | Entry ЛР 5 | Аналітичне читання | Morgan Analyst | GET Alice details | 200 без зайвих mutation прав | 200; PUT/DELETE 403 | A03R; HTTP Analyst 200 |
| A-04 | Добрий | Усі чужі mutation | Alice; Bob incident | PUT і DELETE | 403; state без змін | Обидва 403; snapshot рівний | A04 theory; Bob before/after |
| A-05 | Добрий | Обмежений список | Alice Reporter | List | Лише Alice records | Набір id дорівнює DB owner scope | A05; HTTP list |
| A-06 | Добрий | Вертикальне підвищення | Reporter; власний New | POST triage + forged role | 403; status без змін | 403; snapshot рівний | A06; HTTP/рис. 4 |
| A-07 | Добрий | Дозволений transition | Morgan; New | POST triage | 200, New → Triaged | 200; history +1; repeat 403 | A07; HTTP transition |
| A-08a | Добрий | Overposting ігнорується | Alice; own incident | PUT: owner, role, status, severity | 204; protected state незмінний | 204; content змінено; DB role Reporter | A08a; HTTP state/profile |
| A-08b | Не обрано | Явна відмова надлишку | Обрано контракт A-08a | Той самий forged request | Не застосовується | Не застосовується: поля ігноруються | docs/access-matrix.md |
| A-09 | Добрий | Безпечний audit | Reporter triage denied | Зіставити logger і response | Без secrets; traceId однаковий | Allowlist полів; trace збігається | A09; audit txt; рис. 4 |
| A-10 | Лише 404 | Однакова маскувальна 404 | Обрано політику 403 | Foreign vs missing | Не застосовується | Foreign 403; missing 404 за контрактом | docs/access-matrix.md |
| A-11 | Відмінний | Розширений finding | Обрано рівень Добре | Search та summary | Окремий finding не заявлено | Спільний scope перевірено A05; окремий finding не виконувався | A05; межа рівня |
| A-12 | Добрий | Child та internal | Alice; comment-2/3; Morgan | Direct child + details | Чужий/internal 403; Analyst 200 | 403/403/200; проєкції узгоджені | A12; HTTP child; розділ 7 |

## Audit і browser flow

[A-09 audit](lab04-audit.txt) має той самий traceId, що й [HTTP-відповідь](lab04-http-evidence.json); без credentials/cookie/raw body. Browser flow нового Reporter: login → власний інцидент → прямий UUID Bob → 403. Safe sinks textContent/createTextNode. Тимчасовий власний інцидент видалено після перевірки.

![Власний інцидент у browser client](screenshots/lab04/01-browser-own.png)

![Чужий UUID у browser client](screenshots/lab04/02-browser-denied.png)

![PoC read до виправлення](screenshots/lab04/03-poc-read.png)

![PoC mutation до виправлення](screenshots/lab04/04-poc-mutation.png)

![Retest після виправлення](screenshots/lab04/05-fixed-deny.png)

![Audit та response correlation](screenshots/lab04/06-audit.png)

![Фактична збірка та тести](screenshots/lab04/07-tests.png)

## Відтворення

Windows, .NET SDK 10.0.303, PostgreSQL у Compose project securelab-lab4-verification, порт 54331.

```powershell
./scripts/verify-lab04.ps1
# Перевірка і сервер
./scripts/verify-lab04.ps1 -Rounds 1 -Serve
```

[Журнал](lab04-tests.txt): Release build 0 errors/0 warnings; двічі по 70 passed, 0 failed, 0 skipped; 20 Lab04 scenarios. TRX збережено локально у TestResults. Для seed login задайте власний SeedUsers__Password поза Git до запуску; без нього доступна реєстрація нового Reporter.

## Межі й залишковий ризик

403 розкриває факт існування identifier, проте не вміст. A-10 не застосовується. CSRF cookie mutation - навчальний борг ЛР 6. Concurrency не заявлено. Search/summary одержали спільний scope для узгодженості; окремий finding A-11 рівня «Відмінно» не виконувався.

