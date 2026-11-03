# Огляд секретів і журналів ЛР 6

Дата: 2026-11-03. SDK: 10.0.303. Review-start HEAD: `cfe2d6bd7664112994684a03c279cd0a1e90e299`.

## Межі та спосіб перевірки

Перевірено всі шляхи, повернуті `git ls-files -z`: поточні робочі версії tracked files та read-only знімок тих самих шляхів через `git show HEAD:<path>`. Незакомічені зміни не видаються за новий commit. HEAD є початковою версією для порівняння; довільна історія, stash, інші клони, видалені refs, external CI і локальні untracked секрети не перевірялися.

Команди: `git rev-parse HEAD`; `git ls-files -z`; для кожного шляху `git show HEAD:<path>`; `git check-ignore -v infra/.env src/SecureLab.Api/appsettings.Local.json local.pfx`. Усі read-only Git-команди завершилися з кодом 0. Скрипт локально читає bytes кожного tracked file, декодує UTF-16 за BOM та решту тексту як UTF-8, рахує keyword matches, перевіряє сигнатури private keys/provider tokens/JWT і формує лише path/line/класифікацію, не копіює значення. Відтворення: `python ../output/lab06/source-review/audit_tracked.py` з кореня проєкту; скрипт збережений поза Git у батьківському workspace.

Охоплено 124 tracked files; текстових 118 (зокрема 4 UTF-16 BOM), бінарних PNG 6. У поточному tracked-text знайдено 378 рядків (521 входжень keyword); у HEAD-text 490 входжень. Сигнатур private key/AWS access key/GitHub token/OpenAI-style key/JWT: 0. Це евристичний пошук, а не доказ відсутності невідомих форматів секретів.

Бінарні tracked PNG не можна перевірити keyword-пошуком: вони є попередніми навчальними screenshots; цей текстовий висновок сам по собі не підтверджує відсутності даних усередині зображень. Нові скріншоти ЛР6 перевіряються окремо перед включенням у звіт.

## Рішення щодо конфігурації

| Перевірка | Спостереження та рішення | Межа |
|---|---|---|
| appsettings.Development.json, infra/compose.yaml, SecureLabDesignTimeDbContextFactory.cs | Узгоджені відкриті credentials локальної навчальної БД; accepted, study-only. README забороняє повторне використання. Compose публікує БД на 127.0.0.1 | Не є дозволом на аналогічний tracked credential в іншій БД/середовищі |
| infra/.env.example | Лише POSTGRES_PORT=54329 та пояснення; password/key відсутні | Не перевіряє untracked .env |
| .gitignore | .env та .env.*, виняток !.env.example; appsettings.Local.json, *.pfx, *.pem, *.key, dumps і logs виключені. check-ignore підтвердив 3 контрольні шляхи | Ігнорування не вилучає раніше tracked файл |
| SeedUsers__Password | Назва конфігураційного ключа; seed/tests читають runtime значення з environment; scripts можуть генерувати значення в пам’яті | Runtime environment не включено до звіту |
| Потенційний реальний витік | У перевіреному тексті реальний credential не встановлено; знайдені study/test/reference збіги класифіковано нижче | Видалення з останнього файла не відкликає credential: при реальному витоку потрібні rotation/revocation та перевірка доступних copies/history/logs |

## Статичний огляд logging та audit

`AuthenticationEvents.Write` записує event/result/time/verified subject reference/trace; не приймає password, cookie, token чи request body. Failed login використовує `unverified`; `Lab03Identity.LoginAsync` передає лише перевірений user ID після успіху. `IncidentAccess.Denied` має structured subject/action/resource/result/time/trace без entity dump. Дозволений triage у `IncidentMutationEndpoints.cs` записує subject/action/resource/old/new/result/time/trace після збереження; відмова проходить через `Denied` до зміни. Параметри query logging обмежені allowlist/typed IDs; повний довільний user input не конкатенується у повідомлення. У поточному runtime-коді не знайдено `EnableSensitiveDataLogging`, middleware повного body logging чи запису auth headers.

Статичний review покриває successful login, failed login, denied action, validation error, unexpected error та privileged transition на рівні джерел/дизайну. Він не підміняє runtime-доказ: дозволений Analyst triage, Reporter denial без зміни, safe500 та збіг TraceId мають окремі фактичні докази в lab06-evidence. Внутрішні exception details припустимі лише у захищеному diagnostic sink, у звіт копіюється мінімальний безпечний фрагмент. Загальний висновок про всі production logs не робиться.

## Класифікація всіх рядків зі збігами

Кожен рядок нижче є окремим збігом broad keyword search. Значення навмисно не копіюються. CODE/TEST/DOCUMENTATION позначають контекст перевіреного рядка, а не автоматичне приховування credential. Кількість за класами: CODE=94, DOCUMENTATION=152, IGNORE=1, PACKAGE=2, SCRIPT=18, STUDY=7, SYNTHETIC=10, TEST=94.

| Path і рядок | Keyword | Перевірка і рішення |
|---|---|---|
| `.gitignore:14` | secret | IGNORE: правило виключення локальних secret files |
| `README.md:22` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:47` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:71` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:93` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:109` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:114` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:180` | password | STUDY: відкритий локальний приклад; не використовувати поза loopback стендом |
| `README.md:183` | connectionstring | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:260` | connectionstring | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `README.md:263` | cookie, password, token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/access-matrix.md:43` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/access-matrix.md:45` | cookie, password, token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/access-matrix.md:53` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/architecture.md:72` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/architecture.md:74` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/architecture.md:78` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/architecture.md:82` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/architecture.md:89` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/architecture.md:116` | connectionstring | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:3` | cookie, password, token | SYNTHETIC: тестове значення або опис навчального сценарію |
| `docs/browser-security-evidence.md:22` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:52` | cookie, password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:54` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:61` | cookie, password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:74` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:92` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:93` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:95` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:111` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:112` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:113` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:117` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:119` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:145` | secret | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:150` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:152` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/browser-security-evidence.md:154` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/devtools-records.md:57` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/devtools-records.md:306` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/git-workflow.md:33` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/git-workflow.md:57` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/git-workflow.md:123` | cookie, token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/git-workflow.md:124` | private key | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/git-workflow.md:186` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:30` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:47` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:55` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:56` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:57` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:58` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:59` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:62` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:63` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:67` | cookie, password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:73` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:77` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:78` | cookie, password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:79` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:80` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:83` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:85` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:87` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:92` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:100` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:104` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:106` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:110` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:115` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:122` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:124` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:125` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:126` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:131` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:141` | authorization, cookie, password, token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:146` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:150` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:152` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:163` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:166` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:170` | cookie, password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:172` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:181` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:184` | cookie, password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-report.md:186` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:15` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:40` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:43` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:44` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `docs/lab03-tests.txt:45` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:46` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:47` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:48` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:58` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:60` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:61` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab03-tests.txt:62` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab04-audit.txt:1` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab04-source-manifest.json:48` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab04-source-manifest.json:53` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab04-source-manifest.json:57` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/before.json:634` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/before.json:818` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/final.json:1636` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/final.json:1838` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/final.json:1965` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/nocredentials.json:150` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/nocredentials.json:341` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:39` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:42` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:45` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:49` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:51` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:53` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:55` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:175` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:180` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:182` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:183` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt:201` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Program.cs.txt:16` | connectionstring | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Program.cs.txt:21` | connectionstring | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Program.cs.txt:55` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Program.cs.txt:61` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Program.cs.txt:62` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-source-manifest.json:58` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-source-manifest.json:64` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/lab05-source-manifest.json:68` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-04.md:27` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-04.md:37` | secret | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-04.md:44` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-04.md:70` | password | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-04.md:74` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:13` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:45` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:124` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:126` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:148` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:150` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:154` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:171` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:180` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:228` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:236` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:263` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:281` | authorization, cookie, token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:297` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:350` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-lab-05.md:364` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-template.md:118` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-template.md:131` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-template.md:143` | cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/report-template.md:148` | token | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/source-to-sink.md:3` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/source-to-sink.md:47` | authorization | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `docs/source-to-sink.md:57` | authorization, cookie | DOCUMENTATION: опис механізму або очищений попередній доказ; без збережених session values |
| `infra/compose.yaml:9` | password | STUDY: відкритий локальний приклад; не використовувати поза loopback стендом |
| `scripts/verify-lab03.ps1:6` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab03.ps1:7` | connectionstring | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab03.ps1:15` | connectionstring, password | STUDY: відкритий локальний приклад; не використовувати поза loopback стендом |
| `scripts/verify-lab03.ps1:16` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab03.ps1:30` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab03.ps1:35` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab03.ps1:36` | connectionstring | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab04.ps1:5` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab04.ps1:6` | connectionstring | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab04.ps1:14` | connectionstring, password | STUDY: відкритий локальний приклад; не використовувати поза loopback стендом |
| `scripts/verify-lab04.ps1:15` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab04.ps1:31` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab04.ps1:36` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab04.ps1:37` | connectionstring | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab05.ps1:5` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab05.ps1:6` | connectionstring | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab05.ps1:14` | connectionstring, password | STUDY: відкритий локальний приклад; не використовувати поза loopback стендом |
| `scripts/verify-lab05.ps1:15` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab05.ps1:31` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab05.ps1:37` | password | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `scripts/verify-lab05.ps1:38` | connectionstring | SCRIPT: назва змінної/конфігурації або генерування password у пам’яті |
| `src/SecureLab.Api/Application/Incidents/IncidentAccess.cs:33` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs:12` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs:31` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs:42` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs:53` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs:73` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs:97` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Client/auth-lab.html:29` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Client/auth-lab.html:40` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Client/auth-lab.js:66` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Client/auth-lab.js:72` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Client/auth-lab.js:87` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Client/auth-lab.js:94` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DatabaseBootstrap.cs:35` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:39` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:42` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:45` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:49` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:51` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:53` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:55` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:175` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:180` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:182` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:183` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/DbSeeder.cs:201` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.Designer.cs:35` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.Designer.cs:139` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.Designer.cs:155` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.Designer.cs:168` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.Designer.cs:197` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.Designer.cs:412` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.cs:52` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.cs:154` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.cs:164` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.cs:166` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.cs:210` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/SecureLabDbContextModelSnapshot.cs:32` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/SecureLabDbContextModelSnapshot.cs:136` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/SecureLabDbContextModelSnapshot.cs:152` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/SecureLabDbContextModelSnapshot.cs:165` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/SecureLabDbContextModelSnapshot.cs:194` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/Migrations/SecureLabDbContextModelSnapshot.cs:409` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/SecureLabDbContext.cs:29` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/SecureLabDesignTimeDbContextFactory.cs:9` | connectionstring | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/SecureLabDesignTimeDbContextFactory.cs:10` | password | STUDY: відкритий локальний приклад; не використовувати поза loopback стендом |
| `src/SecureLab.Api/Data/SecureLabDesignTimeDbContextFactory.cs:14` | connectionstring | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/SecureLabDesignTimeDbContextFactory.cs:15` | connectionstring | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Data/SecureLabDesignTimeDbContextFactory.cs:17` | connectionstring | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Contracts/AuthenticationContracts.cs:3` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Contracts/AuthenticationContracts.cs:4` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:12` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:36` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:53` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:59` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:76` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:84` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:86` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs:88` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:13` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:15` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:35` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:37` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:45` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:48` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:67` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:69` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs:78` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Program.cs:25` | connectionstring | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Program.cs:30` | connectionstring | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Program.cs:36` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Program.cs:100` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Program.cs:106` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Program.cs:107` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Program.cs:120` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs:13` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs:63` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs:65` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs:156` | authorization, cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:19` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:20` | token | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:21` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:22` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:24` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:25` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:26` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:27` | cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:52` | authorization, cookie | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:61` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:62` | authorization | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:80` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:103` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:106` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs:121` | password | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/appsettings.Development.json:2` | connectionstring | CODE: ім’я API/поля/параметра/конфігурації; значення runtime не записано |
| `src/SecureLab.Api/appsettings.Development.json:3` | password | STUDY: відкритий локальний приклад; не використовувати поза loopback стендом |
| `tests/SecureLab.Api.Tests/AuthenticationMechanicsTests.cs:8` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/AuthenticationMechanicsTests.cs:10` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/AuthenticationMechanicsTests.cs:11` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/AuthenticationMechanicsTests.cs:12` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:6` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:8` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:9` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:11` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:16` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:18` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:25` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:29` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:31` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:32` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:34` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:35` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:36` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:37` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/CookieClient.cs:40` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/D7ForwardedForTests.cs:69` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/IncidentEndpointTests.cs:17` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab02ContractTests.cs:22` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab02ContractTests.cs:25` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab02ContractTests.cs:27` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab02SearchTests.cs:22` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab02SearchTests.cs:24` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab02SearchTests.cs:71` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab02SearchTests.cs:73` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:32` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:44` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:46` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:62` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:64` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:76` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:79` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:81` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:82` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:86` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:87` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:98` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:99` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs:104` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03CookieProfileTests.cs:8` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03CookieProfileTests.cs:17` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03CookieProfileTests.cs:23` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03CookieProfileTests.cs:28` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03CookieProfileTests.cs:30` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03CookieProfileTests.cs:32` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:20` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:30` | cookie | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:49` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:50` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:52` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:56` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:60` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:64` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:78` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs:79` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:22` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:27` | password | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:28` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:41` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:42` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:43` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:57` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:62` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:70` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:75` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:90` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:91` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:92` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:100` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:109` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:110` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs:111` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03SeedTests.cs:12` | password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab03SeedTests.cs:29` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:18` | authorization | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:47` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:72` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:87` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:98` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:99` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:142` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:156` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:174` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:189` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:190` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:217` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:249` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:251` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:252` | token | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:262` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:270` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:271` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:292` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:293` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs:305` | authorization | SYNTHETIC: тестове значення або опис навчального сценарію |
| `tests/SecureLab.Api.Tests/Lab05BrowserPolicyTests.cs:82` | authorization | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/Lab05BrowserPolicyTests.cs:84` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/SearchMechanicsTests.cs:10` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/SearchMechanicsTests.cs:19` | cookie, password | TEST: перевірка auth/cookie/token або штучний fixture; runtime секрет береться з environment |
| `tests/SecureLab.Api.Tests/packages.lock.json:208` | secret | PACKAGE: назва/метадані пакета, не credential |
| `tests/SecureLab.Api.Tests/packages.lock.json:291` | secret | PACKAGE: назва/метадані пакета, не credential |

## Знімок перевірених tracked файлів

SHA-256 нижче фіксують bytes HEAD та робочої копії, прочитані під час перевірки. Паралельні наступні правки можуть змінити working hash; це не commit і не позначка виконаного final Git state.

| Path | HEAD SHA-256 | Working SHA-256 |
|---|---|---|
| `.config/dotnet-tools.json` | `22b9f78a6a9c0518c57b5f1c9d24f2a5ee4015d3edad56001e1444db07db0e3a` | `22b9f78a6a9c0518c57b5f1c9d24f2a5ee4015d3edad56001e1444db07db0e3a` |
| `.gitignore` | `167e5592cd5b2ffa22e111ee5f0ad9e0cda7768b92b3b91ab31feb098b727baf` | `167e5592cd5b2ffa22e111ee5f0ad9e0cda7768b92b3b91ab31feb098b727baf` |
| `.scaffolds/lab-02.json` | `a2fb593e62b62491d3944bd9737888c65df9179d56564f959d3982a06b3aa0ef` | `a2fb593e62b62491d3944bd9737888c65df9179d56564f959d3982a06b3aa0ef` |
| `.scaffolds/lab-03.json` | `eb7d0ffbb1ed060c0184b109b219eaa5b6af4b35c533a4cca3b5ebb828b29ce2` | `1ae916dd682efe88182a2b7ee959aeb5604d08565832d89280b12fadcbd0cde1` |
| `Directory.Build.props` | `bd804c930df623514df1088dbd732265e74cbb17e52b7a361fa22c253cb31720` | `bd804c930df623514df1088dbd732265e74cbb17e52b7a361fa22c253cb31720` |
| `LICENSE` | `37b43ac24cec0e93a9d3d3184096caaebf5c2a2fe3ba102cee26b62b04b81743` | `37b43ac24cec0e93a9d3d3184096caaebf5c2a2fe3ba102cee26b62b04b81743` |
| `README.md` | `ea40c9875c5e0b73b9c82d99b38868133f4d7f0f3f1918bc9dbb02c079beb868` | `a9efaa00969d3216524b76424b9a062f081fc459e0c0bd7e5835b68ac19b032f` |
| `SecureLab.sln` | `bdeda731df4fd7f034bc1daca1419fa3bca87c10d1ed8fb6535f842fd64b2565` | `bdeda731df4fd7f034bc1daca1419fa3bca87c10d1ed8fb6535f842fd64b2565` |
| `def.md` | `bcc3f362ec67354d199cf4ef7faff88c67f35565d410c18b5c3cb941535e9d13` | `655df7df2c636204f036b0a35d3e7b78404011d68d500dcb4ebcfc3bec31b026` |
| `demonstrations/cors-probe/index.html` | `a4a7e4e0d0d70b018c6ce6b0c008a7e7800cf8af007a8942550a6482f39cbb42` | `a4a7e4e0d0d70b018c6ce6b0c008a7e7800cf8af007a8942550a6482f39cbb42` |
| `demonstrations/cors-probe/probe.js` | `2c718df73f41221544242b53d9a2b6b13dd56bb22588a955fb0df99673115357` | `2c718df73f41221544242b53d9a2b6b13dd56bb22588a955fb0df99673115357` |
| `docs/access-matrix.md` | `c7931f9158545b40c627157dfc73fdf52530ce41640134e6846bb89737c17c9f` | `c7931f9158545b40c627157dfc73fdf52530ce41640134e6846bb89737c17c9f` |
| `docs/architecture.md` | `26694d60b8808bc718de89f490402a8899f5307182117e12ac421d96213a74e3` | `717654dd928e81eb5e8e96795568f09a62f58be8d79d84644b372bb262966cb2` |
| `docs/browser-security-evidence.md` | `871a300c90ce8c67255c827781fd29a11a7cda7c6863d025403804c725620bcd` | `871a300c90ce8c67255c827781fd29a11a7cda7c6863d025403804c725620bcd` |
| `docs/devtools-records.md` | `81763c72cec08141ed7e8836639368648f235878a8e871c213b5655e92dc508d` | `910f62d1fb807472120529f97b0df23b842eb0e212d541a010fd63beddb60123` |
| `docs/git-workflow.md` | `141bee49ad701d3c62c064916d1a598822e4e7d4a5d90b39a4e4e20c0672cb85` | `141bee49ad701d3c62c064916d1a598822e4e7d4a5d90b39a4e4e20c0672cb85` |
| `docs/http-evidence-after.txt` | `e43d12b97f736d9ec1bab97f194dea228b3ce9a9b0b4e922c7e2b06cfa8e9926` | `8e19aad6d9bf874623c3a32c2aeca75cd5043564a47f3839e56e5678717a2927` |
| `docs/lab03-report.md` | `1abd27bfc602415380499aea30983b58f22aa9deaa28fe135f859d2ccac093c4` | `1abd27bfc602415380499aea30983b58f22aa9deaa28fe135f859d2ccac093c4` |
| `docs/lab03-tests.txt` | `e53256b9aeb8ffeb9d95c0767e009eacc4e189521a111053f4624cc8a48c6a5c` | `49c0a0d1c399f0744a17a06313a2dd0a022721553a1d505d00f606696b08d12f` |
| `docs/lab04-audit.txt` | `f94ed1d2a1b392f8f9f14d9db88bcf4fe342dff4bbfdfcf720d9bd02ad011404` | `f94ed1d2a1b392f8f9f14d9db88bcf4fe342dff4bbfdfcf720d9bd02ad011404` |
| `docs/lab04-http-evidence.json` | `852eef41b08d30ee5972d52c08be4b1e4c3e0b2c59138ad6cea6b38dd1aacac0` | `6db2b7baadb907916f86d24d80a73c0277a207d7917f047af559be46b0d81935` |
| `docs/lab04-source-manifest.json` | `a4670de95a4f7925027656a6c37bc5c002b0db8efb8be7e3f5fd644a9a8aa00b` | `7b09b1b4d7e26fcecfb11a65e694af13736ce4278a39632e97535d0b3373f571` |
| `docs/lab04-tests.txt` | `093d05b7fb568d35f7d9236f3f109ee318a708f4b748703e2d3ad3c7839aec15` | `7256d93d38ba0eb4f53c7bd871bb142359604f1154ed764980a9dc64f4c1d8a2` |
| `docs/lab05-evidence/before.json` | `3ac79e8991081d2463e850acd5947644432155adea5635a6b611be8646b9d26b` | `3ac79e8991081d2463e850acd5947644432155adea5635a6b611be8646b9d26b` |
| `docs/lab05-evidence/db-marker-after-safe.txt` | `b553b8975019ac425f207a2800917ed672174be824f70ee66cb9689a73a3d555` | `7c4c8028979a48fcd9c8a0754167fb81977b69213899deedceed9954edb72a13` |
| `docs/lab05-evidence/direct-http.txt` | `eb8626768b77908614435775723ff164ce2bc7c354ea11dc2ebdc9d8240b9086` | `f32abbbeefba05a95e030b36cffe508a73a27a4e4f0163d0c24edbae466ecc5d` |
| `docs/lab05-evidence/final.json` | `ad92f3b219553a6d7e7d4a05662271dd7237b5154170393aba5b6ae27dbe086f` | `ad92f3b219553a6d7e7d4a05662271dd7237b5154170393aba5b6ae27dbe086f` |
| `docs/lab05-evidence/nocredentials.json` | `7532fe54021472dedcd6de2e8c6521647c8358748a380350d52c9ea826106a9a` | `7532fe54021472dedcd6de2e8c6521647c8358748a380350d52c9ea826106a9a` |
| `docs/lab05-evidence/safe.json` | `4d5164db27950f41366a0b8b2759325f759aeb1c8a60fc86a351d78c6012a5de` | `4d5164db27950f41366a0b8b2759325f759aeb1c8a60fc86a351d78c6012a5de` |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Client/browser-lab.html.txt` | `d6d0c546f1dfc372e160fa28390976e93d67cae29296d8278d51399c35100570` | `d6d0c546f1dfc372e160fa28390976e93d67cae29296d8278d51399c35100570` |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Client/browser-lab.js.txt` | `04d9e0c572df239889a011968be7cf0eb5b76a6ab5d7b25669c27ae87a25f145` | `04d9e0c572df239889a011968be7cf0eb5b76a6ab5d7b25669c27ae87a25f145` |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Data/DbSeeder.cs.txt` | `4216e46c8d32b0f0a3d34a9bc563ff01df3e5f620010b51b2f4e858a36633642` | `b0e2bcc0525cef819ebac5466951c275309f8adaf31b2d29ac6342c1bb0a9334` |
| `docs/lab05-evidence/vulnerable-snapshot/src/SecureLab.Api/Program.cs.txt` | `a83090239ee9d2fc4e857d41c0476d68bb09a1a3d5dde66a5b0f9439643f04ef` | `a83090239ee9d2fc4e857d41c0476d68bb09a1a3d5dde66a5b0f9439643f04ef` |
| `docs/lab05-evidence/vulnerable-source-manifest.json` | `5f3815382d5a7ade21cc2acc15b19ac3dd17a790bdd737a621dc8f7888d09350` | `c48b27a9d940670d1ab8326c5837cbbd221900e1b30cec414fd3e0dcca5b797d` |
| `docs/lab05-marker.txt` | `7aba82ae72345b0cc9449d2ae0dbeffa0852c4aeca6746703b1a8b4d018400e6` | `a03ae43adf758dc9b6e5e1de61f14399ec37bc1579617e86a7740c2045c8ac21` |
| `docs/lab05-source-manifest.json` | `aa1f271cc468f33f614c60a46977a87e3d2dddb9c9260c4b91f6fa324d31c65e` | `19626df9e19f2b52738340223b88a56e0c14ea65ebf2999843ad8b78f1ee491b` |
| `docs/lab05-tests.txt` | `80a279c9efee1e6ff9579076d98a3b0970bc109e1117f055d760e299e4ce3596` | `62f9472c73d6c003cd30275a47ed01c7763da09681e1aacf51959fe6c6a18a03` |
| `docs/report-lab-04.md` | `862b32889ee3a4a933585590ebd4b1ff521430580e56820d86b9b01389db1b57` | `4927b005b64996613e3fa9c36d4faf6da362e8dc05f9bc87ceb48b1ceb9810c8` |
| `docs/report-lab-05.md` | `d078367a73bdafe20415bcdd46eacd907a18ef55e2e2c6ef2b3674eb3f4f9141` | `6ef7f6a702dce9ba7069d420a87c0580229ff8140235d48aba25b4d8174acaec` |
| `docs/report-template.md` | `6e20ccc3198ebd9ac928125f7290bb040fcd5fed442fb86f8b117be9aee946c2` | `1046f9fca66325a385d0899e91eb4e2f0cfee30505806866ca1a66af54ee079d` |
| `docs/screenshots/01-client-initial.png` | `4f600228e8a6b8b260fb5c803b1f7e47121724f369b5ee498546184b19a3aee1` | `4f600228e8a6b8b260fb5c803b1f7e47121724f369b5ee498546184b19a3aee1` |
| `docs/screenshots/02-details-script-as-text.png` | `648059c9ab4f1eaa2697f5f3e67d4859383fd536068bd8e07c10cbe81298a4f1` | `648059c9ab4f1eaa2697f5f3e67d4859383fd536068bd8e07c10cbe81298a4f1` |
| `docs/screenshots/03-summary-loaded.png` | `4477cd93274e41fe381c373569eefe1e284abe09b49a34305039efa9e61d8410` | `4477cd93274e41fe381c373569eefe1e284abe09b49a34305039efa9e61d8410` |
| `docs/screenshots/04-summary-safe-error.png` | `ef28aed49e1f91801245d5b9bac2f2b3a72df7b44d7f172c37c737f5a363a4f9` | `ef28aed49e1f91801245d5b9bac2f2b3a72df7b44d7f172c37c737f5a363a4f9` |
| `docs/screenshots/05-summary-empty.png` | `f57a46fd7b211050adf6b6f42ee7ec0ec65bf5002a6c3a3ec38f249ba2dd19a8` | `f57a46fd7b211050adf6b6f42ee7ec0ec65bf5002a6c3a3ec38f249ba2dd19a8` |
| `docs/screenshots/06-scalar-openapi.png` | `cfa8c1c0b7e5ea19c0519e2f7a9d669d7812f17b2513934a947dafeaabecf3e5` | `cfa8c1c0b7e5ea19c0519e2f7a9d669d7812f17b2513934a947dafeaabecf3e5` |
| `docs/source-to-sink.md` | `f507e3fc8d951d01a0ba611ca39a7fa467cc25942528cce6b55e90061f1141fa` | `f507e3fc8d951d01a0ba611ca39a7fa467cc25942528cce6b55e90061f1141fa` |
| `docs/t03-a-diff.txt` | `2df8ed15b6c1f169e0c6bd4020d8de5248c65468aebf67c1272ec6b6ca43c1a5` | `34c5fcca32b48988744a8713282f1d015d4e38007e2259faa211e89fe1a5757b` |
| `docs/t03-a-response-1.txt` | `59461bad13621e89c6e0d0e651465431c728c860c8815b26561e4b41ef867b10` | `59461bad13621e89c6e0d0e651465431c728c860c8815b26561e4b41ef867b10` |
| `docs/t03-a-response-2-first.txt` | `27c7612faa11d1ad03696af124a08c257b187bd2c43539ca77784ed70fe2588f` | `27c7612faa11d1ad03696af124a08c257b187bd2c43539ca77784ed70fe2588f` |
| `docs/t03-a-response-2-second.txt` | `2914aa280ef3bc8892585acbb071c38678f2fe8040d70bce0a1aa952aa1b45ac` | `2914aa280ef3bc8892585acbb071c38678f2fe8040d70bce0a1aa952aa1b45ac` |
| `docs/t03-a-tests.txt` | `a821a92213de3a2d9ebcc0071a08418e692afdc796f77af1ba2734131e0db3f2` | `a821a92213de3a2d9ebcc0071a08418e692afdc796f77af1ba2734131e0db3f2` |
| `docs/variant-02-a.md` | `a6005627766afd6fabc72f5a03686dd4582f8d4400fc5c1bfa12ad447ae41dc8` | `a6005627766afd6fabc72f5a03686dd4582f8d4400fc5c1bfa12ad447ae41dc8` |
| `global.json` | `20c93ca7812d0a9825eef71217156f95ab2f4fe5aadfeb22ebc1c24b630018d3` | `20c93ca7812d0a9825eef71217156f95ab2f4fe5aadfeb22ebc1c24b630018d3` |
| `infra/.env.example` | `d5b5be79b825b437d9d93f4a9b302a0be72cfee31ca0f2c0e2144c62f8cd3c2b` | `d5b5be79b825b437d9d93f4a9b302a0be72cfee31ca0f2c0e2144c62f8cd3c2b` |
| `infra/compose.yaml` | `158255aec9954c6992792f4aa5d769578e78eb19bdbb3a7a603e8ac705d0a394` | `158255aec9954c6992792f4aa5d769578e78eb19bdbb3a7a603e8ac705d0a394` |
| `report.md` | `f7314ab0117865f95b605c79e9838da1849b60ae19e4d43cc0b25cb7bea43cd2` | `f7314ab0117865f95b605c79e9838da1849b60ae19e4d43cc0b25cb7bea43cd2` |
| `scripts/test.sh` | `8d287ba30491ded617a4163a92b7a23925aa9283a671066d83997dcb17a933ad` | `8d287ba30491ded617a4163a92b7a23925aa9283a671066d83997dcb17a933ad` |
| `scripts/verify-lab03.ps1` | `a7d501673d03e629853f4fb75cf2d1d518c47adf9daf0f4b1cceff3d8d1d1051` | `a7d501673d03e629853f4fb75cf2d1d518c47adf9daf0f4b1cceff3d8d1d1051` |
| `scripts/verify-lab04.ps1` | `d628f3cd01282ec175956b970ebeaf3ec90d8375a04cc5dd46bd0a8411e098cd` | `d628f3cd01282ec175956b970ebeaf3ec90d8375a04cc5dd46bd0a8411e098cd` |
| `scripts/verify-lab05.ps1` | `1c9e7ae3801c46857d2ff07aa20da21168c08eeb7c6b257ea5cf8dc89638a58e` | `1c9e7ae3801c46857d2ff07aa20da21168c08eeb7c6b257ea5cf8dc89638a58e` |
| `src/SecureLab.Api/Application/Incidents/IncidentAccess.cs` | `f48c2dd776fc756174cdc292f7653eea3353b703309214fbff4082e8e526f9c4` | `f48c2dd776fc756174cdc292f7653eea3353b703309214fbff4082e8e526f9c4` |
| `src/SecureLab.Api/Application/Incidents/IncidentQueries.cs` | `002f1848340df733e4558aabf9c8e9df374e7de226b417f1de81d9dbb1daf982` | `41aa6aeac55f6885b4c2dcd39a9c043484824dac33fd7e74a4b01c3f1d647894` |
| `src/SecureLab.Api/Client/app.js` | `4b029cb3a14620f27179a4fec39e71cfbda362d658d1dd037102911b46ed432a` | `4c64fcc9bed4c6fc99bbc721f6e5b4143ef5aeecc6901c0664d83a865d913968` |
| `src/SecureLab.Api/Client/auth-lab.html` | `94edbec79a87714bdcc7d8cc1af8f89bd170f212cfc77f569053aea35888f7f4` | `004a4eb7c49ca4bcbfa3d67e7f3ed4fb3f82647ac4c5b77f6e9051a65bd0e882` |
| `src/SecureLab.Api/Client/auth-lab.js` | `e719cae4cd609dda660d531c4734bacf1ec5b12c5c6f41bfe7e2fed546ed83a1` | `116ad18a43890f6a9cfc3a3e4b678e5779155294162ce2ddf0c536fb1c5d5077` |
| `src/SecureLab.Api/Client/browser-lab.css` | `84fca2e72b47dfa2a444747dc9461e15db345499888050d0468e670118fc9b86` | `84fca2e72b47dfa2a444747dc9461e15db345499888050d0468e670118fc9b86` |
| `src/SecureLab.Api/Client/browser-lab.html` | `3193c2c324f288581611914db0540aeb316610ccc6c3922968609c16c3223f65` | `bfe87d5fe3fab4a064c64ef242dc5a68cc4ae2a035668dafcb78bae79747373e` |
| `src/SecureLab.Api/Client/browser-lab.js` | `0f95f78b17694f6068a30b16f27082575bbdeb477a78e0027807936676e7c44e` | `325b70795d28b923b3a7ca8acceb35401a0b68e343aa6ca2cd6e8c40b27cad43` |
| `src/SecureLab.Api/Client/index.html` | `ca6d654f2ca88dfe872c94e9cc372dd9e7e64c8dce8e09832f9bc8bfde3c2c63` | `8ba5be3d96a29f0bd87b2efb4c24b226e1c1f19f0cd62a99cfe40eedbca08253` |
| `src/SecureLab.Api/Client/styles.css` | `5f1cdaa8cba2183665db17d7df7c7b3a9b22ecdea55f6c6523d40963a831c37c` | `75268daec76dfaa86d07f1a004381b8b960b77d12295204221d269e87884d301` |
| `src/SecureLab.Api/Data/DatabaseBootstrap.cs` | `92bab21d47386fedc0c6a61d074345dae4e44093886f9524f8474d427a2b8309` | `bf8d94a377d553f9ff48b88922372b9c015b9d6583364bf1a8e0636e850811b3` |
| `src/SecureLab.Api/Data/DbSeeder.cs` | `4216e46c8d32b0f0a3d34a9bc563ff01df3e5f620010b51b2f4e858a36633642` | `b0e2bcc0525cef819ebac5466951c275309f8adaf31b2d29ac6342c1bb0a9334` |
| `src/SecureLab.Api/Data/Entities/ApplicationUser.cs` | `1ed7d6d7c592fbee6a5371f3d7347a11e276ff296b18ecc18ec3969ba6f80953` | `1ed7d6d7c592fbee6a5371f3d7347a11e276ff296b18ecc18ec3969ba6f80953` |
| `src/SecureLab.Api/Data/Entities/Incident.cs` | `2b4d7cb6afc0f873c9813b7ed8e6d93300660ec5468832a55704f4cc4080e466` | `1dab8dd41487d23d4f82316ced97a3d8e019b36a72c8bab07b4d53e4b4f3b667` |
| `src/SecureLab.Api/Data/Entities/IncidentComment.cs` | `866ec90a16292287b14333f204bea92f11cb7a8d1216784add2db706f664ad79` | `cc4a9079f8829f312e812a79639fa55db54d98476db96b7013bb0d47f00d308e` |
| `src/SecureLab.Api/Data/Entities/IncidentStatusHistory.cs` | `a675f5060f63d2021d88b9d35027ba869336e08d0037a3c9fc684c56788721d6` | `52fa6f8624b4df840ff310774e566ffdd7d6204abf7ca294464587a92247df11` |
| `src/SecureLab.Api/Data/Entities/StudyUser.cs` | `af6c158c9b11f7487744218e84efc99e5fbe4177f5e2b1cb5519bedc88547e81` | `af6c158c9b11f7487744218e84efc99e5fbe4177f5e2b1cb5519bedc88547e81` |
| `src/SecureLab.Api/Data/Migrations/20260803173310_InitialCreate.Designer.cs` | `067601dff5629f7783587475bf87c44c7e01cb03d42627b3e8ecc763ea50414c` | `067601dff5629f7783587475bf87c44c7e01cb03d42627b3e8ecc763ea50414c` |
| `src/SecureLab.Api/Data/Migrations/20260803173310_InitialCreate.cs` | `80061ea009d8270075637ffef7ad6adf4f669432d82128f3077267aebbb474c4` | `80061ea009d8270075637ffef7ad6adf4f669432d82128f3077267aebbb474c4` |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.Designer.cs` | `f3bb8aef231db2a055c819564266ea6cfc55ba8fbbcab1aae89fb072883a2e9c` | `f3bb8aef231db2a055c819564266ea6cfc55ba8fbbcab1aae89fb072883a2e9c` |
| `src/SecureLab.Api/Data/Migrations/20260902062130_Lab03Identity.cs` | `2bf267c5e3c518208b415abdf7526b8afbdc0272a9393fa4baa7fb7fa7a6c08c` | `2bf267c5e3c518208b415abdf7526b8afbdc0272a9393fa4baa7fb7fa7a6c08c` |
| `src/SecureLab.Api/Data/Migrations/SecureLabDbContextModelSnapshot.cs` | `3f8bd61c378866a848969b14b0716f62b37c7ed2d37e3a1ee78c9ca899f485ee` | `3f8bd61c378866a848969b14b0716f62b37c7ed2d37e3a1ee78c9ca899f485ee` |
| `src/SecureLab.Api/Data/SecureLabDbContext.cs` | `08ee579ea4a0e87a337dc56fba00097edf06ec54298e17839ed2b7f16123e354` | `08ee579ea4a0e87a337dc56fba00097edf06ec54298e17839ed2b7f16123e354` |
| `src/SecureLab.Api/Data/SecureLabDesignTimeDbContextFactory.cs` | `a077ee89fc4aba9d1ee4aa9daad0ca7818b159c6fa45cb10a7a0d6d6b0954bb4` | `a077ee89fc4aba9d1ee4aa9daad0ca7818b159c6fa45cb10a7a0d6d6b0954bb4` |
| `src/SecureLab.Api/Presentation/Contracts/AuthenticationContracts.cs` | `0caa1929b86edda39b0609ee28e4eafec556b2b873a287114e362136b8468954` | `0caa1929b86edda39b0609ee28e4eafec556b2b873a287114e362136b8468954` |
| `src/SecureLab.Api/Presentation/Contracts/IncidentResponses.cs` | `ec193cdd0b6072a757b4b891ccd4dde483842af35a0801d5de8dfaeb9aff248b` | `83f4e7236ff4b6e1277d12a8893c4bd63791766b4424b09a8b4065cf24e8ed07` |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentEndpoints.cs` | `ce7ded67355891452d63eeaed4eca32fd6d94140f5e7cc67c1607022a2a068b1` | `3f047f366780b236f4a46886edd81c285ec4784062565781bf901f73065330ab` |
| `src/SecureLab.Api/Presentation/Endpoints/IncidentMutationEndpoints.cs` | `b6ca392bed254958066851e7bf15d79b859d073a28a3eaf05dc8a29d821dcd5b` | `b91ccc3d03c3d9588707a7d75019225bd83a09fd85b2ed305d804ef5e214ff91` |
| `src/SecureLab.Api/Program.cs` | `569df01eaa66aa9ffa81358d24ffde14762be5d1afb97dac087c12d38ff64489` | `0adf237602b3f03eb211e8fc6cd57571c4e03675b8d4ed982fb375dcdaea8485` |
| `src/SecureLab.Api/Properties/launchSettings.json` | `44be66e6f09892581dd19357bf4bcf19f2e2938375902f951711e806da4f9a45` | `44be66e6f09892581dd19357bf4bcf19f2e2938375902f951711e806da4f9a45` |
| `src/SecureLab.Api/Scaffolding/AuthenticationEvents.cs` | `9d70aefc864b7d260cebf9a179513c05f98bb2637c99274f74e2ac457df280c5` | `9d70aefc864b7d260cebf9a179513c05f98bb2637c99274f74e2ac457df280c5` |
| `src/SecureLab.Api/Scaffolding/Lab02Endpoints.cs` | `5236f809b4fe72137366ae25ea7aa44d522ed369327a6124a18dd537fda7193d` | `f2a7cc3b9859e113ecccb6775a3f592827fd832e2bdb11d6fef2d584182ccb60` |
| `src/SecureLab.Api/Scaffolding/Lab02Seed.cs` | `8f301690af63caa666cb9f33a19cff9be51109fa846224b1b8f2c15a7ad1da2e` | `8f301690af63caa666cb9f33a19cff9be51109fa846224b1b8f2c15a7ad1da2e` |
| `src/SecureLab.Api/Scaffolding/Lab03Identity.cs` | `d6c1b57cb30d1a786fe26d7fa455b4dda4fa549d34cdbe9964316c25434fbe74` | `1597e66d4176dbf74833df8c4c785a02fe628982bafede06c99330f32813e816` |
| `src/SecureLab.Api/Scaffolding/LoginRateLimit.cs` | `2f4c132d5de7ca212d8bfb8ddd6068a3fa9240a838f2613f925592061023b5ef` | `274ef34cec6f479397fb56a8cdb450663313314687c862f943599f9d5ea53ed2` |
| `src/SecureLab.Api/SecureLab.Api.csproj` | `42ea04e8b558d4d633a8c1cbbb23d9266abe57d825f57140302b2e2400731144` | `42ea04e8b558d4d633a8c1cbbb23d9266abe57d825f57140302b2e2400731144` |
| `src/SecureLab.Api/appsettings.Development.json` | `a9e05682437acb292ce9cb28a3171213e4b935602ff7bb73c87f85459fd72d59` | `a9e05682437acb292ce9cb28a3171213e4b935602ff7bb73c87f85459fd72d59` |
| `src/SecureLab.Api/appsettings.json` | `1114b215e51ffc155916ee7bea2c4192b097fc7f1bc095635ff55dd5af15cc57` | `1114b215e51ffc155916ee7bea2c4192b097fc7f1bc095635ff55dd5af15cc57` |
| `src/SecureLab.Api/packages.lock.json` | `fd531d8bc1c8dbdf66ec6c0dc9a59433301f529531652804897280f270b1b6d6` | `cd785f23e31f38a17be1239a43124711720916103c7b91bf78156f147e42edaf` |
| `tests/SecureLab.Api.Tests/AssemblyInfo.cs` | `3f7edd72eac91614fcf49f709dd4e560c74ebef8da68a53a02947e5611cd1869` | `3f7edd72eac91614fcf49f709dd4e560c74ebef8da68a53a02947e5611cd1869` |
| `tests/SecureLab.Api.Tests/AuthenticationMechanicsTests.cs` | `76be6e0cfe188a0e21a7fa2eaacf10031e8a5eb41856a5f62db38a0bc7023464` | `76be6e0cfe188a0e21a7fa2eaacf10031e8a5eb41856a5f62db38a0bc7023464` |
| `tests/SecureLab.Api.Tests/CookieClient.cs` | `29236a8989361a9625462f6d182644cd9f0d8f415d7a3b1d54548d9396e07539` | `c16585c50d348b290569ea593b3ff163bb1b620eef8a15f91740965ffacbee67` |
| `tests/SecureLab.Api.Tests/D7ForwardedForTests.cs` | `1fdcf16663c9b66a4b030a2d1e470ae38170f18ab92ef473f0b1e715f023aa7c` | `ce5e0fae3e76afc0d2c44724f2a74916778eb313e23da9ce5928055024ee9af6` |
| `tests/SecureLab.Api.Tests/IncidentEndpointTests.cs` | `6c1b2e02e3da580aaf065cba3079360a3c39a843471510dee8dd629912c712a4` | `6c1b2e02e3da580aaf065cba3079360a3c39a843471510dee8dd629912c712a4` |
| `tests/SecureLab.Api.Tests/Lab02ContractTests.cs` | `d73d53ee5319a775235a915e5da2fa4b2e2942c9c194e58f9e70a78e002d5d76` | `3064e619765904752c820b47d53534c0f8091fdd131e3838dfb556f393bdcf13` |
| `tests/SecureLab.Api.Tests/Lab02SearchTests.cs` | `ceb3a6b87dd5b4fab64aa2c4ee8a481e01574ff08a001a2c961b746b924f28e6` | `fe91edafb1b8071dedee77cacb1c2c365778156d8603cd5717835f146436f52d` |
| `tests/SecureLab.Api.Tests/Lab03AuthenticationTests.cs` | `10ec1d6bd26f45e978d634014c5cbf775c43b01e5e22eb67cdfd4451aeac98be` | `10ec1d6bd26f45e978d634014c5cbf775c43b01e5e22eb67cdfd4451aeac98be` |
| `tests/SecureLab.Api.Tests/Lab03CookieProfileTests.cs` | `091df6caaa6f71d837a80bc79dfb108a843cf7383a4b5f208a267ffb373b8291` | `091df6caaa6f71d837a80bc79dfb108a843cf7383a4b5f208a267ffb373b8291` |
| `tests/SecureLab.Api.Tests/Lab03RateLimitAndEventsTests.cs` | `9a9f22e5a2b0e8c2e93b9e122f09316cbbc07a614c6684d6ba59f302314c29fe` | `a2c96fa7087040e4a69a91a5f7a3054c46a4c206d5698f60d63fce54e4fb929e` |
| `tests/SecureLab.Api.Tests/Lab03RegistrationTests.cs` | `ac6c78673fff2f9de0286ea1ba003a3bda1f0ed55a653484c3ec011cd6e836f1` | `ac6c78673fff2f9de0286ea1ba003a3bda1f0ed55a653484c3ec011cd6e836f1` |
| `tests/SecureLab.Api.Tests/Lab03SeedTests.cs` | `86eb8ad74e598010ae893f5a25eb5a7172ae91a4eecd55f9dff16bc2c06d24a3` | `86eb8ad74e598010ae893f5a25eb5a7172ae91a4eecd55f9dff16bc2c06d24a3` |
| `tests/SecureLab.Api.Tests/Lab04AuthorizationTests.cs` | `c78fa24c719a478c36c712fd687b47dd33f304caf7037d4bc33662d664151b8d` | `1c098da99aee81406134aaea1d3551a562aeedeb76eed9cb469aeaeeaea0f9b0` |
| `tests/SecureLab.Api.Tests/Lab05BrowserPolicyTests.cs` | `62c6fb6bc0716b5a3f3f6a9fa52afa62c71160182d6fc8412f20ac7f63316762` | `62c6fb6bc0716b5a3f3f6a9fa52afa62c71160182d6fc8412f20ac7f63316762` |
| `tests/SecureLab.Api.Tests/SearchMechanicsTests.cs` | `a63367f9798afbeca147bee493a5f7e989bfe7329a15a76b6cfd7dc30ba6398b` | `a63367f9798afbeca147bee493a5f7e989bfe7329a15a76b6cfd7dc30ba6398b` |
| `tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj` | `d2756478b3a9248d4b9354f9a8e329022aa141a40d4407561e82c99ea14009ef` | `d2756478b3a9248d4b9354f9a8e329022aa141a40d4407561e82c99ea14009ef` |
| `tests/SecureLab.Api.Tests/SecureLabApiFactory.cs` | `5fc6cfd14d4df0f2ae0c627ebeb6feef8973401b7774db6dfc0795db58fef4c8` | `5fc6cfd14d4df0f2ae0c627ebeb6feef8973401b7774db6dfc0795db58fef4c8` |
| `tests/SecureLab.Api.Tests/Usings.cs` | `50436eb3a60162e0cfad749eddfb06f8908f821a90ca44532df946a53e071359` | `50436eb3a60162e0cfad749eddfb06f8908f821a90ca44532df946a53e071359` |
| `tests/SecureLab.Api.Tests/packages.lock.json` | `75b65f09b5e6baede6deed646c2f4e1d236f8dc4f0cf483952fb521dd4fb40c3` | `70212dfd88060462b11ae546742b842d1d4b4f01e16f020c83fded554b6e210f` |
| `tests/http/incidents.http` | `01812150c7aad0e1adb611c8f3f8eb5b836412f8038de9a21cab4be4f6bf7a34` | `cf4f86efffecfafe74db8dd646e9e2a948e5481d97bf1b11e01090acd3f12619` |
| `tests/http/lab-02-checks.http` | `5c3f4436778ca2a4b704dcc0c907fa3db2e9898cfd008fc80d9ff8de461c7506` | `5c3f4436778ca2a4b704dcc0c907fa3db2e9898cfd008fc80d9ff8de461c7506` |
| `tests/http/lab-02-search.http` | `1cd952b7eb9e7967889f1ca72fa8810f408b064d1324ee8c5b4408b2a81e9b75` | `1cd952b7eb9e7967889f1ca72fa8810f408b064d1324ee8c5b4408b2a81e9b75` |
| `tests/t03-a-1.json` | `aae557932f5f12c4df5bd44a8b9583af3b17be7454e41f356edcf94d5d05df99` | `71f0694c6f8effa57e54e68b8fc5fa40e778a889af6139b81eea1357d1275e2a` |
| `tests/t03-a-2.json` | `deb5e4ad4cefd30e32fe8641411e063dea9f353a810295f0d71e8fd9d68afaff` | `049aad6b53f437d3a7468f995bd1ecf452c053d40ee7b2a2b481c6c99fbb8749` |
