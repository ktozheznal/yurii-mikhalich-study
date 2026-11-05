## Прогноз до перевірки

| Policy / клієнт | Origin / site відносно API | Credentials mode | Прогноз доставки GET | Прогноз читання JS | Межа висновку про mutation |
|---|---|---|---|---|---|
| Початкова / O | Cross-origin / same-site | `include` | Очікується доставка за доступного API та чинного сеансу | Очікуються `200` і непорожній token | Виконання mutation не доведене |
| Звужена / O | Cross-origin / same-site | `include` | Простий GET може дійти; фактичну доставку потрібно встановити окремо | Очікується CORS-відмова читання response | Виконання mutation не доведене |
| Початкова / normal client | Same-origin / same-site | `same-origin` | Очікується доставка | Очікуються `200` і непорожній token | Перевіряється лише token flow |
| Звужена / normal client | Same-origin / same-site | `same-origin` | Очікується доставка | Очікуються `200` і непорожній token | Перевіряється лише token flow |


## Причинна таблиця: actual до та після

| Case ID / policy | Origin / site | Credentials: режим і підтвердження | Доставка GET / HTTP-статус | Читання response JavaScript | Межа висновку про mutation |
|---|---|---|---|---|---|
| `O-before` / початкова | Cross-origin / same-site | `include`; успішну автентифікацію підтверджено `200` на authenticated token-route | GET отримав відповідь `200` | Так; JSON прочитано, token непорожній | Mutation не надсилалася; її виконання не доведене |
| `O-after` / звужена | Cross-origin / same-site | `include`; cookie jar збережено; фактична доставка credentials окремо не встановлена | **Невідомо** за наявним evidence | Ні; отримано `TypeError` і повідомлення браузера про CORS | Mutation не надсилалася; її виконання не доведене |
| `same-origin-before` / початкова | Same-origin / same-site | `same-origin`; успішну автентифікацію підтверджено `200` | GET отримав відповідь `200` | Так; token непорожній | Перевірено доступність token-route |
| `same-origin-after` / звужена | Same-origin / same-site | `same-origin`; успішну автентифікацію підтверджено `200` | GET отримав відповідь `200` | Так; token непорожній | Перевірено доступність token-route |


