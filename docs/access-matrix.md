# Матриця доступу ЛР 4

Baseline 2-A, рівень «Добре». Головна дія — details/read. Усі endpoint-и
інцидентів і коментарів вимагають verified Identity principal. `X-Demo-UserId`,
owner/role з body або query не визначають права.

| Дія | Анонім | Reporter | Analyst | Administrator | Реалізація та доказ |
|---|---|---|---|---|---|
| List | 401 | Лише власні | Усі для аналізу | Порожній список | IncidentAccess.Scope; A-05 |
| Details | 401 | Власні 200; чужі 403 | 200 для аналізу | 403 | CanRead; A-02/A-03/A-03R |
| Create | 401 | 201, owner з principal | 201, owner з principal | 201, owner з principal, без додаткових прав на нього | Успадкований create ЛР 3 |
| Update content | 401 | Власні 204; чужі 403 | 403 | 403 | CanEdit; A-02/A-04/A-08a |
| Delete | 401 | Лише власні у New: 204; решта 403 | 403 | 403 | CanDelete; A-02/A-04 |
| POST triage | 401 | 403 | Лише New → Triaged: 200; решта 403 | 403 | CanTriage; A-06/A-07/A-09 |
| Public comment за прямим id | 401 | 200 після доступу до parent; чужий 403 | 200 після доступу до parent | 403 | CanRead(parent); A-12 |
| Internal comment | 401 | 403 навіть власнику parent | 200 після доступу до parent | 403 | CanReadInternal; A-12 |
| Search і severity-summary | 401 | Лише власні | Усі для аналізу | Порожній результат або нульові групи | Scope до Take/GroupBy; A-05 |

Administrator не має неявного обходу ownership. Додаткового audit/export flow
у проєкті немає. Create зберігає успадкований контракт для authenticated
accounts; він не надає права читання/редагування поза цією матрицею.
Delete дозволено лише для власної нової заявки, щоб не видаляти матеріали,
які вже опрацьовує аналітик. Звичайний update змінює тільки title/description.
Severity, status, owner і role цим endpoint-ом не змінюються.

## Контракт відмов

- Анонімний protected request — 401 без HTML redirect.
- Існуючий чужий або недозволений ресурс — 403 `application/problem+json`.
- Неіснуючий incident/comment — 404. Для політики 403 A-10 не застосовується.
- Для update, delete, triage і child використано ту саму політику 403.
- A-08a: невідомі JSON поля ігноруються. Власний regular update з коректними
  title/description дає рівно 204; owner/role/status/severity лишаються незмінними.
- Triage не приймає бажаний status: сервер установлює тільки Triaged після New.
- Deny виконується до зміни tracked entity та SaveChangesAsync.

403 навмисно розкриває факт існування identifier; це прийнята політика локального
стенда. UUID не є контролем доступу. Для приховування існування потрібні однакові
404 та A-10.

## Безпечний audit

`authorization.denied`: subject (verified user id), action, resource identifier,
result=deny, UTC, traceId. TraceId збігається з Problem Details цього запиту.
Немає password, cookie, token, raw body чи тексту інциденту.
А-09 захоплює реальні ILogger events через WebApplicationFactory.

## Межа роботи

Read/update/delete/child/triage та A-01–A-09, A-12 виконуються для «Добре».
Search/summary отримали те саме обмеження для узгодженості наявного клієнта.
Окремий finding A-11 і concurrency не заявляються як рівень «Відмінно».
CSRF захист cookie mutation залишається темою ЛР 6.
