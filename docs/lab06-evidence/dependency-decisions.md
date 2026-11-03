# Рішення щодо залежностей ЛР 6

Дата перевірки: 2026-11-03. SDK: `10.0.303`, stable .NET 10. Review-start HEAD: `cfe2d6bd7664112994684a03c279cd0a1e90e299`; перевірено поточну незакомічену робочу копію solution. Пакети під час цього огляду не змінювалися.

## Фактичний аудит

Команда: `dotnet package list --project SecureLab.sln --vulnerable --include-transitive --no-restore`.

Перший sandbox-запуск: exit 1, NuGet service index недоступний через `No such host is known`. Цей результат не є висновком про вразливість пакетів. Повторний запуск поза sandbox, виконаний основним агентом для цього самого solution: exit 0; джерело `https://api.nuget.org/v3/index.json`; для `SecureLab.Api` та `SecureLab.Api.Tests` отримано `has no vulnerable packages given the current sources`. Повний фактичний output і результат locked restore зберігаються в `dependency-audit.txt` у цій теці.

Рішення: **залишити поточні зафіксовані версії**. Відомих advisories для direct і transitive graph на дату цього запуску джерело не повідомило; advisory-specific update, mitigation або suppression не потрібні. Відсутність advisories не підтверджує відсутності невідомих вразливостей і не замінює аналіз коду/досяжності API. Під час майбутньої зміни пакета слід оновити lockfile узгоджено та повторити restore, build і regression pack.

## Direct package graph та рішення

Версії нижче звірено з поточними `.csproj`; transitive graph включено до команди аудиту.

| Проєкт | Package | Version | Тип | Advisory / severity | Relevant functionality | Рішення |
|---|---|---|---|---|---|---|
| API | Microsoft.AspNetCore.OpenApi | 10.0.10 | direct | Не повідомлено | Development OpenAPI generation | Залишити |
| API | Microsoft.OpenApi | 2.7.5 | direct override | Не повідомлено | OpenAPI model; наявний явний override | Залишити наявний override |
| API | Microsoft.EntityFrameworkCore | 10.0.10 | direct | Не повідомлено | ORM/query pipeline | Залишити |
| API | Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.10 | direct | Не повідомлено | Identity stores | Залишити |
| API | Microsoft.EntityFrameworkCore.Relational | 10.0.10 | direct | Не повідомлено | Relational EF Core | Залишити |
| API | Microsoft.EntityFrameworkCore.Design | 10.0.10 | direct, PrivateAssets | Не повідомлено | Development migration tooling | Залишити |
| API | Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | direct | Не повідомлено | PostgreSQL EF provider | Залишити |
| API | Scalar.AspNetCore | 2.16.16 | direct | Не повідомлено | Development API UI | Залишити; route доступний лише в Development |
| Tests | coverlet.collector | 6.0.4 | direct, PrivateAssets | Не повідомлено | Test coverage collector | Залишити |
| Tests | Microsoft.AspNetCore.Mvc.Testing | 10.0.10 | direct | Не повідомлено | In-process integration harness | Залишити |
| Tests | Microsoft.NET.Test.Sdk | 17.14.1 | direct | Не повідомлено | Test execution | Залишити |
| Tests | xunit | 2.9.3 | direct | Не повідомлено | Assertions and tests | Залишити |
| Tests | xunit.runner.visualstudio | 3.1.4 | direct, PrivateAssets | Не повідомлено | Test adapter | Залишити |
| Обидва | Решта graph із packages.lock.json | За lockfile | transitive | Не повідомлено поточним джерелом | Не оцінювалося окремо за відсутності advisory | Залишити; повторити audit після зміни graph |

`dotnet nuget why` потрібний для аналізу шляху конкретної проблемної transitive dependency. Нових advisories не отримано, тому вигаданий package/advisory для цієї команди не підставлявся. Наявний коментар у `.csproj` про історичний Microsoft.OpenApi override не є новою finding ЛР6.

## Межі й подальша перевірка

`Directory.Build.props` містить `TreatWarningsAsErrors=true` і `RestorePackagesWithLockFile=true`. Нові `NuGetAuditSuppress`, `NoWarn` чи винятки `NU1901–NU1904` під час ЛР6 не додавалися. Рішення temporary acceptance відсутні, тому owner/date/compensating control для придушення не застосовуються.

Перевірка охоплює .NET/NuGet graph цього solution та advisories поточного джерела на зазначену дату. Вона не є окремим аудитом Docker base image, ОС, браузера, невідомих advisories чи всіх upstream source files. Відповідальний за наступний перегляд — власник навчального проєкту; повторний audit потрібний перед зміною dependency graph або реальним deployment. Коміти, теги, branch changes, push і автоматичні package updates не виконувалися.
