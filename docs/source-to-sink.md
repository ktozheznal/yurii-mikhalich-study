# Карта source to sink ЛР 5

Спільний сценарій 2-A, рівень «Добре». Обидва досліджувані поля є звичайним текстом; formatted HTML не підтримується. Authentication та authorization ЛР 4 збережено. Стан до виправлення надано у scaffold `lab-05-start-v1`; виправлення не змінює API-контракт або збережене значення marker.

Нових commits і тегів не створено за прямою вказівкою користувача. Git HEAD на початку роботи: `419d5aca741b89eae99e32910c72479e39c4de64`, наявна гілка `lab5`. SHA-256 маніфести станів є ідентифікаторами файлів, а не commit hashes. Остаточний статус перевірок наведено в [доказах](browser-security-evidence.md).

## Incident Description

Локальний негативний контроль: інцидент `672816db-e333-410a-9834-dd0a68ab8d3b`, створений Alice. Вхід скопійовано без кінцевого переносу з [канонічного marker](lab05-marker.txt).

| Крок | Файл і функція або DTO | Конкретне значення | Довіра та доказ |
|---|---|---|---|
| Source | `Client/browser-lab.html`, форма `#edit`, поле `name=description` | Текст користувача Alice | Недовірений ввід; форма не визначає, як його інтерпретувати |
| Request | `Client/browser-lab.js`, submit handler | `JSON.stringify({title, description, severity: "Low", occurredAtUtc})`, `POST /api/incidents` | JSON серіалізація зберігає символи, але не робить їх безпечним HTML |
| Validation | `Scaffolding/Lab02Endpoints.cs`, `MapLab02Endpoints`, `CreateIncidentRequest` | `request.Description`, непорожнє значення, довжина до 4000 символів, `Trim()` | Сервер перевіряє предметний контракт; `<`, `>` та лапки не заборонено. Для High/Critical додатково потрібно 40 символів; scaffold створює Low |
| Mapping | Той самий endpoint, initializer `new Incident` | `Description = description!`; owner тільки з перевіреного principal | Авторизація і прив'язка owner не надають довіри вмісту Description |
| Storage | `Data/SecureLabDbContext.cs`, entity `Incident` | `incidents.description`, `HasMaxLength(4000)` | PostgreSQL зберігає рядок як дані. Для marker `Trim()` не змінює значення |
| Response | `Presentation/Endpoints/IncidentEndpoints.cs`, `GetDetailsAsync`; `Application/Incidents/IncidentQueries.cs`, `GetDetailsAsync` | `IncidentDetailsResponse.Description` → JSON `description` | Доступ перевіряє `IncidentAccess`; Alice і Analyst Morgan читають, Bob отримує 403. JSON залишається недовіреним для DOM |
| Client transform | `Client/browser-lab.js`, `load()` | `current = await response.json()`, потім `current.description` | JSON parser повертає рядок; HTML sanitation або encoding тут не відбувається |
| Sink до fix | Виданий `browser-lab.js` | `#description.innerHTML = current.description` | HTML context: браузер створював `img` з inline handler. Morgan: `GET: 200`, 1 image, marker `executed`; [before.json](lab05-evidence/before.json) |
| Sink після fix | `Client/browser-lab.js`, `renderText(target, value)` | `renderText(#description, current.description)` → `target.textContent = value ?? ""` | Text context: структура DOM не залежить від рядка. Підтвердження safe state і final state наведено окремо в доказах |

`</>` у звичайному тексті не слід вилучати заради XSS. Required/length/severity/date/duplicate-title перевірки залишаються чинними; безпечне відображення забезпечує sink. У JSON лапка може бути записана як `\"`, а в DOM serialization `<` як `&lt;`; ці записи не означають зміни самого рядка.

## Incident Comment Text

Другий негативний контроль: публічний seed-коментар `30000000-0000-0000-0000-000000000005` до інциденту Alice `20000000-0000-0000-0000-000000000001`. Автор Morgan, `IsInternal=false`, час `2026-08-01 09:35:00 UTC`. Його текст дорівнює тому самому канонічному marker. Наявні коментарі не видалено.

| Крок | Файл і функція або DTO | Конкретне значення | Довіра та доказ |
|---|---|---|---|
| Source | `Data/DbSeeder.cs`, `SeedAsync` | `IncidentComment.Text` містить marker | Навчальний seed моделює текст автора коментаря; це не довірений HTML |
| Request | Reset/seed, а не новий HTTP endpoint | `dotnet run ... -- --reset-database` в окремій навчальній БД | Endpoint створення коментарів у поточному API відсутній; не вигадується новий POST-контракт |
| Validation і mapping | `DbSeeder.SeedAsync`, initializer `new IncidentComment` | Визначені GUID, parent, author, `Text`, `IsInternal=false`, UTC | Для seed немає окремого request validator. `required string Text` задає вимогу C# initializer, а не runtime HTML-захист |
| Storage | `Data/SecureLabDbContext.cs` | `incident_comments.text`, `HasMaxLength(2000)` | Фактична схема обмежує 2000 символами; marker у межах ліміту. DB також не надає HTML-довіри |
| Response | `IncidentQueries.GetDetailsAsync`, `IncidentCommentResponse` | `comments[].text`; сортування за `CreatedAtUtc` | Публічний marker-коментар доступний Alice і Morgan. Internal comment фільтрується для Reporter незалежно від safe rendering |
| Client transform | `Client/browser-lab.js`, цикл `for (const comment of current.comments ?? [])` | `comment.text` | Рядок із response передається до render без перетворення змісту |
| Sink до fix | Виданий `browser-lab.js` | `paragraph.innerHTML = comment.text`, потім append у `#comments` | Створювався `p > img`; Morgan: 1 image, marker `executed`, `GET: 200`; [before.json](lab05-evidence/before.json) |
| Sink після fix | `load()` і той самий `renderText` | `createElement("p")`, `renderText(paragraph, comment.text)`, `append(paragraph)` | Структуру створює код, текст додає helper. `replaceChildren()` очищує попередній список без HTML parser |

## Інвентаризація контекстів

| Місце | Дані | Контекст і рішення |
|---|---|---|
| `browser-lab.js`, Description і comments | Недовірений plain text з API | Єдиний `renderText` з `textContent`; жодного `innerHTML` |
| `browser-lab.js`, title та status | Назва з API, HTTP status | `textContent`; значення не розбираються як HTML |
| `browser-lab.js`, textarea | Description для редагування | Властивість `value`, звичайне значення форми |
| `browser-lab.js`, incident route | GUID з поля | `encodeURIComponent` і фіксований same-origin шлях `/api/incidents/`; authorization виконує сервер |
| `Client/app.js`, `createTextElement` | Текст штатного списку і деталей | `createElement` плюс `textContent`; той самий принцип безпечного текстового контексту |
| `demonstrations/cors-probe/probe.js`, result | HTTP response або повідомлення про CORS | `textContent`; response не вставляється як HTML |

## Finding і межі виправлення

Активом є браузерний сеанс користувача, який переглядає інцидент. Передумова: автор має право зберегти plain text, а жертва має право прочитати parent incident. Першопричина: два місця `innerHTML` перетворювали дозволені дані на HTML, хоча їхній контракт визначає plain text. Локально marker виконував тільки зміну `data-lab05-marker` на передбаченому елементі. Це stored XSS із клієнтським DOM sink; ці класифікації сумісні.

Виправлення змінює межу інтерпретації через `textContent`, зберігає початкові рядки та працює для довільного HTML-like тексту. CSP додано після окремого retest safe sink без CSP, щоб блокування handler policy не маскувало невиправлений sink.

Залишковий ризик: новий unsafe sink або зміна контексту на URL, attribute чи JavaScript вимагатимуть окремого рішення. Safe text helper не є універсальним URL sanitizer. Cookie mutation endpoints ще потребують CSRF-механізму наступної роботи; CORS і CSP не замінюють authorization.
