## Прогноз

Перед запуском записав, які відповіді очікую до зміни правила та після неї.

| Запит | До зміни правила | Після зміни правила |
|---|---|---|
| (1) Невідома спроба входу | 409 Conflict | 201 Created |
| (2), перше виконання нового унікального title | 201 Created | 201 Created |
| (2), повторне виконання того самого title | 409 Conflict | 409 Conflict |

## Результати ручної перевірки

| Запит | Отриманий статус |
|---|---|
| (1), перше виконання: Entry is illegal, severity = Medium | 201 Created |
| (1), додаткове повторення: Entry is illegal | 409 Conflict |
| (2), перше виконання: Title duplicated, severity = Low | 201 Created |
| (2), друге виконання: Title duplicated | 409 Conflict |

Також повторив перший запит: після створення інциденту API повернув 409. Для другого заголовка перша спроба дала 201, а друга — 409.

### Тіло запиту (1)

Перший запит надіслав із файлу `tests/t03-a-1.json`.

```json
{
    "title": "Entry is illegal",
    "description": "You are trying to enter the system from an unknown device.",
    "severity": "Medium",
    "occurredAtUtc": "2024-06-15T10:30:00Z"
}
```

### Тіло запиту (2)

Другий запит двічі надіслав із файлу `tests/t03-a-2.json` без зміни тіла.

```json
{
    "title": "Title duplicated",
    "description": "Incident with such title already exists.",
    "severity": "Low",
    "occurredAtUtc": "2024-06-15T10:30:00Z"
}
```

### Запит (1): початкова відповідь 201

Під час першого виконання отримав таку відповідь:

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Mon, 12 Oct 2026 20:30:32 GMT
Server: Kestrel
Location: /api/incidents/3ecde388-5c45-4fd0-93ec-b750e6b00508
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer

{"id":"3ecde388-5c45-4fd0-93ec-b750e6b00508","title":"Entry is illegal","severity":"Medium","status":"New","occurredAtUtc":"2024-06-15T10:30:00+00:00","createdAtUtc":"2026-10-12T20:30:33.143763+00:00"}
```

### Запит (1): додаткова повторна спроба

Після повторного надсилання отримав конфлікт:

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json
Date: Mon, 12 Oct 2026 20:31:11 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Інцидент уже існує","status":409,"detail":"Інцидент із таким заголовком уже є серед незакритих.","traceId":"00-80a7916d70b18ec60220281ca44edc3b-fe50ae704defc879-00"}
```

### Запит (2): перше виконання

Новий заголовок успішно створився:

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Mon, 12 Oct 2026 20:32:13 GMT
Server: Kestrel
Location: /api/incidents/89547e3e-ebb2-4685-bb72-54e9f4fd9006
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer

{"id":"89547e3e-ebb2-4685-bb72-54e9f4fd9006","title":"Title duplicated","severity":"Low","status":"New","occurredAtUtc":"2024-06-15T10:30:00+00:00","createdAtUtc":"2026-10-12T20:32:13.5359778+00:00"}
```

### Запит (2): друге виконання

Повторний запит із тим самим заголовком повернув 409:

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json
Date: Mon, 12 Oct 2026 20:32:32 GMT
Server: Kestrel
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
Referrer-Policy: no-referrer

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Інцидент уже існує","status":409,"detail":"Інцидент із таким заголовком уже є серед незакритих.","traceId":"00-a9320ba9ab7eac9cca028fa52c95a64b-ded4ec66c50e8379-00"}
```

## SQL із консолі

Із журналу `docs/t03-a-api.log` скопіював перевірку конфлікту перед створенням першого інциденту. Значення параметрів у журналі відображаються як `?`.

```text
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (3ms) [Parameters=[@title='?', @duplicatedSince='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      SELECT EXISTS (
          SELECT 1
          FROM incidents AS i
          WHERE i.title = @title AND i.status <> 'Closed' AND i.created_at_utc >= @duplicatedSince)
```

У SQL видно додану умову `i.created_at_utc >= @duplicatedSince`. Параметр `duplicatedSince` містить поточний UTC-час мінус 30 днів. `SELECT EXISTS` підтверджує, що перевірка виконується в БД.

## Перевірка автоматичним тестом

Запустив клас `Lab02ContractTests`, у якому є автоматичний T-03:

```powershell
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release --filter "FullyQualifiedName~SecureLab.Api.Tests.Lab02ContractTests" --logger "console;verbosity=normal"
```

Повний вивід зберіг у `docs/t03-a-tests.txt`. Результати запуску:

```text
Passed SecureLab.Api.Tests.Lab02ContractTests.T02_InvalidDto_ReturnsSafeValidationProblem(scenario: "long-description-before-trim", field: "description") [1 s]
Passed SecureLab.Api.Tests.Lab02ContractTests.T02_InvalidDto_ReturnsSafeValidationProblem(scenario: "numeric-severity", field: "severity") [5 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T02_InvalidDto_ReturnsSafeValidationProblem(scenario: "future-date", field: "occurredAtUtc") [21 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T02_InvalidDto_ReturnsSafeValidationProblem(scenario: "empty-title", field: "title") [3 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T02_InvalidDto_ReturnsSafeValidationProblem(scenario: "missing-date", field: "occurredAtUtc") [3 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T02_InvalidDto_ReturnsSafeValidationProblem(scenario: "long-title-before-trim", field: "title") [6 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T06_UnknownIncident_ReturnsSafeProblemDetails [101 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T09_HighDescription_Rejects39AndAccepts40AfterTrim [156 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T01_ValidCreate_NormalizesDateAndIgnoresServerManagedFields [37 ms]
Passed SecureLab.Api.Tests.Lab02ContractTests.T03_DuplicateActiveTitleConflicts_ButClosedAndDifferentCaseDoNot [49 ms]

Test Run Successful.
Total tests: 10
     Passed: 10
 Total time: 2,8406 Seconds
```

Усі 10 тестів пройшли, зокрема T-03. Він залишився зеленим, оскільки створює свіжий інцидент, який потрапляє в межу останніх 30 днів.

## Пояснення

409 означає, що дані запиту коректні, але створення інциденту конфліктує з уже наявним незакритим записом із таким самим заголовком за останні 30 днів. 400 використовується для помилок у самих даних, наприклад порожнього заголовка або відсутньої дати. Додана умова `i.created_at_utc >= @duplicatedSince` прибирає старі інциденти з перевірки, тому seed-запис від 02.08.2026 вже не блокує створення за правилом останніх 30 днів.

## git diff v0.2.0

```diff
diff --git a/src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs b/src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs
index d2795c6..90d57b3 100644
--- a/src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs
+++ b/src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs
@@ -112,9 +112,11 @@ public static class Lab02Endpoints
             {
                 return Results.ValidationProblem(errors);
             }
+            
+            var duplicatedSince = now.AddDays(-30);
 
             var hasActiveTitle = await db.Incidents.AnyAsync(item =>
-                item.Title == title && item.Status != IncidentStatus.Closed, ct);
+                item.Title == title && item.Status != IncidentStatus.Closed && item.CreatedAtUtc >= duplicatedSince, ct);
             if (hasActiveTitle)
             {
                 return Results.Problem(
```
