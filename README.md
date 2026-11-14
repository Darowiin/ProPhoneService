# ProPhoneService

Сервис записи на ремонт телефонов: витрина, заказы на ремонт, личный кабинет клиента и панель мастера.

Модульный монорепозиторий:

```
.
├── client/   # фронтенд (Svelte + TypeScript, Vite) - пока только конфиг и линтер
├── server/   # бэкенд ASP.NET Core Web API + xUnit-тесты
├── docs/     # продуктовая документация, ERD
├── .github/  # CI (GitHub Actions)
├── docker-compose.yml  # PostgreSQL + API
└── .env.example        # шаблон переменных окружения
```

## Стек

| Слой     | Технологии |
|----------|------------|
| Фронт    | Svelte 5 + TypeScript, Vite, ESLint |
| Бэк      | ASP.NET Core (Web API), EF Core 10 + Npgsql, xUnit |
| БД       | PostgreSQL 17 (схема ниже, миграции EF Core) |
| CI       | GitHub Actions |

## Запуск

### Через Docker (PostgreSQL + API)

При первом запуске скопировать `.env.example` в `.env` и заполнить пароль БД, ключ JWT и настройки VK. Существующий `.env` при обновлении сохраняется.

```bash
cp .env.example .env
docker compose config --quiet
docker compose up -d --build --wait --wait-timeout 180
docker compose ps
```

PostgreSQL доступен на порту 5432, API — на 8080. Docker Compose подставляет значения из корневого `.env` в переменные контейнеров:

| Параметр в `.env` | Переменная API | Значение по умолчанию |
|------------------|----------------|----------------------|
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | `Development` |
| `DATABASE_MIGRATE_ON_STARTUP` | `Database__MigrateOnStartup` | `true` |
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | `ConnectionStrings__Default` | Из `.env` |
| `JWT_KEY` | `Jwt__Key` | Из `.env`, не менее 32 байт UTF-8 |

`DATABASE_MIGRATE_ON_STARTUP=true` применяет миграции до запуска HTTP API в любом выбранном окружении; `false` отключает автоматическое применение. Начало и завершение миграций либо их отключение выводятся в логи. При ошибке миграции API не начинает принимать запросы. Без Docker используется `Database__MigrateOnStartup`; если настройка отсутствует, автомиграции включены только в Development.

Healthcheck контейнера API вызывает `GET /health` через `curl`, который установлен в образе. Ответ `Healthy` проверяет работу HTTP API; доступность БД и VK проверяется отдельно. Оба контейнера используют `restart: unless-stopped`: Docker перезапускает их после завершения процесса и запуска Docker, кроме контейнеров, остановленных вручную. Статус `unhealthy` сам по себе не запускает перезапуск. [Описание параметров Compose](https://docs.docker.com/reference/compose-file/services/).

Вне Development обязателен `Jwt__Key`; в Development при его отсутствии используется dev-ключ. Секреты передаются через окружение; локальные `appsettings.Development.json`, `appsettings.Local.json` и `.env` исключены из контекста сборки образа.

Для мастер-аккаунта задать `MASTER_PHONE` и `MASTER_VK_USER_ID` (вне Docker — `Auth__MasterPhone` и `Auth__MasterVkUserId`). Телефон — в формате `+7XXXXXXXXXX`, VK ID — положительный числовой ID доверенного пользователя. При старте создаются клиент, роль Master и привязка к указанному VK. Мастер должен разрешить сообщения сообщества. Без обоих значений мастер не создаётся; неизвестному VK нельзя получить роль Master, указав телефон в гостевой заявке. Старый `MASTER_EMAIL` больше не используется.

### Backend без Docker

```bash
cd server
dotnet restore
dotnet build
dotnet test
dotnet run --project ProPhoneService.Api
```

Connection string для локального запуска - в `server/ProPhoneService.Api/appsettings.Development.json` (в `.gitignore`):

```bash
cp server/ProPhoneService.Api/appsettings.Development.json.example server/ProPhoneService.Api/appsettings.Development.json
```

Пароль в example совпадает с дефолтом из `.env.example`, так что с `docker compose up db` заработает сразу, БД при этом всё равно нужна

Health-check: `GET /health` → `Healthy`.

### Frontend

```bash
cd client
npm ci
npm run dev
```

Open `http://localhost:5173/`. Without `client/.env`, the frontend uses deterministic mock data.

To connect the backend, copy `client/.env.example` to `client/.env` and run any branch backend on port 8080. Vite proxies `/api` there, so local development does not require CORS.

```bash
cd client
copy .env.example .env   # Windows PowerShell; use cp on POSIX
npm run dev
```

For production hosting, serve the built static frontend (`npm run build` → `client/build`) and proxy `/api` to the backend on the same origin.

```bash
npm run lint
npm run check
npm run build
npm run preview
```

### Обновление и проверка тестового стенда

На текущей VM API и PostgreSQL запускаются через Docker Compose, фронтенд доступен на внешнем порту 4173. Проброс внешнего порта 18080 ведёт на порт API 8080 внутри VM. Для браузера используется адрес сайта `http://5.164.142.108:4173` и относительные запросы `/api` через существующий прокси.

В `client/.env` должно быть `VITE_API_URL=/api`. Значение встраивается при сборке, поэтому после его изменения фронтенд нужно пересобрать. `VITE_API_PROXY_TARGET=http://localhost:8080` настраивает прокси Vite при разработке; для размещённого сайта `/api` направляет nginx, сохраняя этот префикс. При одном origin отдельная настройка CORS не требуется.

Обновить API из каталога репозитория на VM:

```bash
cd ~/ProPhoneService
git pull --ff-only
docker compose config --quiet
docker compose up -d --build --wait --wait-timeout 180
docker compose ps
docker compose logs --tail=100 api
curl --fail http://127.0.0.1:8080/health
```

После добавления параметров запуска в существующий `.env` сохранить `ASPNETCORE_ENVIRONMENT=Development` и `DATABASE_MIGRATE_ON_STARTUP=true` для текущего стенда. Изменения переменных применяются через `docker compose up`, обычный `restart` их не перечитывает. Снаружи проверить `http://5.164.142.108:18080/health` и `http://5.164.142.108:4173/api/services`: ожидаются `Healthy` и JSON прайс-листа.

Перезапуск с проверкой сохранности данных:

```bash
docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT count(*) AS orders FROM repair_order; SELECT count(*) AS clients FROM client;"'
docker compose restart db api
docker compose up -d --wait --wait-timeout 180
docker compose ps
curl --fail http://127.0.0.1:8080/health
docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT count(*) AS orders FROM repair_order; SELECT count(*) AS clients FROM client;"'
```

Количество записей до и после должно совпасть, существующие заявки должны открываться через API. Данные PostgreSQL сохраняются в именованном volume `pgdata`; `docker compose down` его сохраняет, а `down -v` удаляет. Логи API и БД: `docker compose logs -f api db`.

Проверка полного входа и обновления сессии через браузер выполняется после подключения интерфейса к актуальным контрактам телефона и VK. Рабочее проксирование `/api` проверяется отдельно от этого сценария.

## Схема БД

Полная ERD: [`docs/ErdDiagram.png`](docs/ErdDiagram.png)

| Таблица                | Назначение |
|------------------------|------------|
| `client`               | клиенты (uuid PK, name, phone, created_at) |
| `device`               | техника (type, manufacturer, model, serial_number) |
| `repair_order`         | заказ на ремонт (FK client_id - null до подтверждения контакта, device_id; sequence_number, status, total_price, problem_description, contact_phone, preferred_visit_at, created_at) |
| `repair_status_history`| история смены статусов заказа (status, changed_at, comment, changed_by - аккаунт мастера) |
| `repair_order_service` | связка заказ↔услуга (FK repair_order_id, service_id; price) |
| `service`              | прайс-лист услуг (name, description, price, is_active, updated_at, updated_by) |
| `account`              | аккаунт входа (FK client_id 1:1; role - Client/Master, created_at) |
| `auth_code`            | одноразовые коды VK (phone, binding_id, code, expires_at, failed_attempts, locked_until) |
| `notification_log`     | лог уведомлений (FK repair_order_id; channel, recipient (VK ID), subject, message, status, attempts, created_at) |

## Архитектура server

```
server/
├── ProPhoneService.Domain.Shared/        # enum-ы, машина состояний, WorkshopOptions (без зависимостей)
├── ProPhoneService.Domain/               # сущности, интерфейсы IRepository, доменные исключения, VisitTimeValidator
├── ProPhoneService.Infrastructure.EfCore/ # DbContext, конфигурации, миграции, реализации репозиториев, аудит
├── ProPhoneService.Application.Contracts/ # DTO-контракты
├── ProPhoneService.Application/          # сервисы (просмотр заявок, создание заявки, изменение статуса заявки, прайс-лист), маппинг Mapster
├── ProPhoneService.Api/                  # контроллеры, DI
└── ProPhoneService.Api.Tests/            # xUnit
```

Слои направлены внутрь: `Api` -> `Application` -> `Domain`, Репозитории - generic `IRepository<T>` + специализированные, scoped lifetime, batch-сохранение через `SaveChangesAsync`

Аудит "кто/когда" - `AuditInterceptor`: проставляет `CreatedAt`/`ChangedAt`/`UpdatedAt` из `TimeProvider` и аккаунт из токена (`changed_by`/`updated_by`) в том же батче, без отдельной таблицы; аккаунт запроса даёт `ICurrentActor` (реализация в API читает клейм `sub`)

## Машина состояний заказа

Смена статуса - только через `RepairOrder.ChangeStatus()`, карта переходов - `RepairStatusTransitions` в `Domain.Shared`
Заказ создаётся фабрикой `RepairOrder.Create(...)`: до инициализации `Status == null`, `ChangeStatus` выбрасывает исключение

```
PendingContact → Created → InProgress → Ready → Completed
Выдача без ремонта: Created → Ready → Completed
Отмена (Cancelled): из PendingContact по сроку подтверждения, из Created/InProgress — мастером
```

Гостевая заявка создаётся в `PendingContact` (клиента и аккаунта ещё нет, `ClientId` пуст) и активируется кодом из VK: при подтверждении создаются клиент и аккаунт, заявка привязывается и переходит в `Created`; до этого мастер её не видит

Каждая смена пишет запись в `repair_status_history` одним батчем. Запрещённый переход -> 409 Conflict. Конфликт параллельных изменений (токен конкурентности `xmin`) ловится в репозитории как `ConcurrencyConflictException` и тоже отдаётся как 409

### Выдача устройства и статистика

Мастер переводит заказ в `Ready` после ремонта или напрямую из `Created`, если устройство выдаётся без ремонта. В обоих сценариях после фактической выдачи мастер вручную устанавливает `Completed` («Выдан клиенту»). Прямой переход `Created → Completed` запрещён и возвращает 409. При `Ready` отправляется уведомление о готовности, при `Completed` сообщение не отправляется.

Дата выдачи — `changedAt` записи истории со статусом `Completed`; отдельное поле даты не вводится. Статистика учитывает оба сценария выдачи и сохранённую сумму `TotalPrice`, включая заказы с нулевой суммой. Незавершённые и отменённые заказы не учитываются. Период определяется датой выдачи, а не датой создания заявки.

Границы месяца и квартала рассчитываются в UTC. Начало периода и текущий момент включаются в диапазон; выдачи с будущей датой исключаются и для периода `All`.

### Номера заявок и названия статусов

После сохранения заявка получает постоянный номер `orderNumber` вида `З-000001`. PostgreSQL выдаёт `sequence_number` через общую последовательность `repair_order_number_seq`, уникальный индекс защищает от дублей. Номер сохраняется при смене статуса и перезапуске приложения, удалённые номера повторно не используются. Пропуски после отката транзакции допустимы; после `З-999999` следует `З-1000000`. UUID `id` сохраняется для маршрутов API и связей в БД.

`orderNumber` возвращается при создании заявки, в списках и детальных карточках панели мастера и ЛК. Поле `status` содержит техническое значение, `statusName` — русское название; история также возвращает `statusName`.

| Статус | Название |
|---|---|
| `PendingContact` | Ожидает подтверждения |
| `Created` | Заявка принята |
| `InProgress` | В ремонте |
| `Ready` | Готов к выдаче |
| `Completed` | Выдан клиенту |
| `Cancelled` | Заявка отменена |

Миграция `AddOrderNumbersAndSimplifyStatuses` создаёт последовательность и поле номера. Поддержка `InDiagnostic` и `Agreed`, преобразование прежних статусов и перенос тестовых данных исключены. При локальном обновлении тестовая БД пересоздаётся с применением всех миграций с нуля.

## Эндпоинты API

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/api/services` | публично | прайс-лист активных услуг (HybridCache, 5 мин) |
| POST/PUT/DELETE | `/api/services...` | Master | правка прайса (скрытие - `is_active=false`) |
| GET | `/api/orders?status=&visitFrom=&visitTo=&page=` | Master | страница заявок панели мастера (фильтр по статусу и желаемой дате визита, пагинация; неподтверждённые не видны) |
| GET | `/api/orders/{id}` | Client (свой) / Master | карточка заказа: клиент, устройство, услуги, тайм-лайн статусов; чужой или неподтверждённый заказ - 404 |
| POST | `/api/orders` | публично | создание заявки: без токена — `PendingContact` и ссылка в VK, с токеном - сразу `Created`; описание проблемы до 2000 символов; 201 + номер |
| GET | `/api/me/orders?status=&page=` | Client | заказы клиента из клейма токена (новые сверху, пагинация; неподтверждённые не попадают) |
| GET | `/api/me/orders/{id}` | Client | карточка своего заказа: устройство, услуги, стоимость, тайм-лайн; чужой - 404 |
| GET | `/api/master/stats?period=Month\Quarter\All` | Master | статистика: выданные заказы по типам устройств и сумма (по моменту перехода в `Completed`) |
| PATCH | `/api/orders/{id}/status` | Master | смена статуса, JSON `{"status":"InProgress"}` без комментария (409 при запрещённом переходе/конфликте) |
| POST | `/api/auth/request-code` | публично | код входа через привязку VK, JSON `{ "phone": "+7XXXXXXXXXX" } |
| POST | `/api/auth/confirm` | публично | подтверждение кода: вход существующего аккаунта; аккаунт и клиент создаются только при подтверждении заявки, иначе 400; JWT + refresh в HttpOnly cookie |
| POST | `/api/auth/refresh` | cookie | ротация refresh-токена (погашенный старый отклоняется) |

## Уведомления и привязка VK

Единственный канал доставки - бот сообщества VK. `MessengerNotifier` использует `VkNotifier`; события сообщества получает фоновый `VkLongPollService` через Bots Long Poll API. Контейнеру нужен исходящий HTTPS-доступ к VK API и серверу Long Poll. Доступ к самому сайту настраивается отдельно.

### Настройка сообщества

1. В управлении сообществом включить сообщения и разрешить пользователям писать сообществу
2. В разделе «Работа с API» создать ключ сообщества с правами для сообщений и управления сообществом, необходимыми для `messages.send` и `groups.getLongPollServer`
3. Включить Long Poll API, выбрать версию API и событие входящего сообщения `message_new`; версия должна соответствовать `VK_API_VERSION`
4. Заполнить локальный `.env` по `.env.example`:

```dotenv
VK_GROUP_ID=123456789
VK_GROUP_TOKEN=your-community-token
VK_API_VERSION=5.199
```

Docker Compose передаёт их как `Messengers__Vk__GroupId`, `Messengers__Vk__Token`, `Messengers__Vk__ApiVersion`. При запуске без Docker используется секция `Messengers:Vk` и переменные окружения с такими именами; `.env` автоматически читает только Docker Compose. В Production настройки VK обязательны. В Development без них API стартует, Long Poll отключается, попытка доставки возвращает ошибку.

Пересоздать API и проверить журнал:

```bash
docker compose up -d --build --force-recreate api
docker compose logs -f api
```

В журнале должно появиться «VK Long Poll запущен». Опрашивать одно сообщество следует одним экземпляром API. Callback API и его секрет/строка подтверждения не используются, регистрация внешнего сервера не требуется.

### API привязки

1. После создания гостевой заявки вызвать `POST /api/messengers/orders/{orderId}/link` с JSON `{}` или `{"channel":"Vk"}`. Телефон берётся из заявки; ссылка доступна только для `PendingContact` и истекает не позднее 5 минут после создания заявки
2. Для аккаунта вызвать `POST /api/messengers/me/link` с JWT и таким же телом. Телефон берётся из профиля текущего аккаунта
3. Ответ содержит `channel`, `url`, `expiresAtUtc`. Ссылка `https://vk.me/club{groupId}?ref={token}` — одноразовый секрет, её нельзя публиковать или логировать
4. Открыть ссылку, начать диалог и отправить сообщение сообществу до истечения срока ссылки. Для привязки VK должен передать `ref` в событии `message_new`; одно открытие страницы без события не завершает привязку
5. Обработчик принимает только личное сообщение нужного сообщества, где положительные `from_id` и `peer_id` совпадают. После сохранения привязки отправляется утверждённое сообщение «Мессенджер успешно привязан!» и код подтверждения

Хэши секретов и получатели хранятся в `messenger_binding`. Действует лимит выдачи ссылок 10 запросов в минуту на IP. Чужой отправитель не может погасить уже использованную ссылку. Новая гостевая заявка не позволяет заменить существующего получателя телефона. Старые привязки неподдерживаемых каналов не участвуют в поиске и доставке; заявки и аккаунты сохраняются.

Long Poll обрабатывает события последовательно. Позиция `ts` продвигается после успешной обработки всей пачки; при ошибке повторяется текущее событие, уже обработанные элементы пачки пропускаются. `failed=1` обновляет позицию, `failed=2` обновляет ключ с сохранением позиции, `failed=3` открывает новую сессию. При ошибках транспорта выполняется повтор через 5 секунд. Токены, ключи Long Poll, payload и текст исключений в логи не выводятся; доступны этап, тип ошибки, безопасный код и стек вызовов.

Позиция Long Poll и текущая пачка хранятся в памяти: после перезапуска открывается новая сессия, гарантии воспроизведения событий за время простоя нет. Если привязка не завершилась, следует получить свежую ссылку и повторить действие. Повторная привязка того же получателя безопасна, но подтверждающее сообщение при повторной доставке события может прийти повторно.

`INotifier.SendAsync(channel, recipientId, text, cancellationToken)` поддерживает только VK. Совместимый метод `SendAsync(target, subject, text, cancellationToken)` принимает телефон, находит привязку VK и отправляет сообщение; email не поддерживается. При отказе провайдера выбрасывается `MessengerUnavailableException`, API отвечает 503 с `offlineOrderAvailable: true` и предложением оформить заказ в мастерской.

После привязки бот присылает подтверждение привязки и шестизначный код. Код действует 5 минут с момента генерации, но заявка должна быть подтверждена в течение 5 минут после её создания: повторный запрос кода не продлевает срок заявки. Только ввод кода на сайте активирует связанную заявку и выдаёт JWT; само сообщение боту не выполняет вход. Другие заявки с тем же телефоном автоматически не активируются.

### Заявка и вход

`POST /api/orders` принимает `name`, обязательный `phone` в формате `+7XXXXXXXXXX`, данные устройства и визита. Email и выбор канала отсутствуют. Гостевой ответ `201` содержит `id`, `requiresContactConfirmation: true` и `messengerLink` с `channel`, `url`, `expiresAtUtc`; клиент открывает `messengerLink.url` и отправляет сообщение сообществу. Авторизованная заявка создаётся сразу в `Created`, без повторного подтверждения и ссылки.

После получения кода вызвать:

```http
POST /api/auth/confirm
Content-Type: application/json

{"phone":"+79001234567","code":"123456"}
```

Успех — `200`, access-токен в ответе и refresh-токен в HttpOnly cookie. Создаются или используются существующие клиент и аккаунт по телефону; заполненное имя не перезаписывается, пустое заполняется именем из заявки. Заявка переходит `PendingContact → Created`, привязка VK переносится на аккаунт. Код погашается атомарно с этими изменениями.

Для повторного входа или повторного кода: `POST /api/auth/request-code` с `{"phone":"+79001234567"}`. Ожидается `204`; без привязки — `400`, при сбое VK — `503`, при повторном запросе ранее 60 секунд — `429`. После 5 неверных кодов контакт блокируется на 15 минут. Сбой доставки не сохраняет новый код и не запускает для него интервал повторной отправки.

`GET /api/me` с Bearer JWT возвращает `id`, `name`, `phone`, `vkLinked`. Выбора и смены канала нет. Существующие endpoints `/api/me/orders` и `/api/me/orders/{id}` возвращают только собственные заказы.

Фоновая проверка каждые 15 секунд отменяет неподтверждённые заявки, которым исполнилось 5 минут, включая заявки из закрытого браузера и оставшиеся после перезапуска API. Статус и запись истории сохраняются атомарно, уведомление не отправляется. Подтверждение после крайнего срока отклоняется сразу, даже до ближайшего фонового прохода.

### Обновление существующей базы

Миграция `UseVkPhoneAuthentication` удаляет email-поля клиента и заявки, аннулирует старые email-коды и связывает новые коды с привязкой VK. Заказы, клиенты, аккаунты и их идентификаторы сохраняются. Перед обновлением существующего стенда сохранить резервную копию; удалённые email и старые коды при откате не восстанавливаются. Если у клиента без телефона сохранилась привязка VK к аккаунту, при успешном входе пустой телефон заполняется из этой привязки. Клиентам без телефона и без такой привязки нужно отдельно назначить корректный телефон для нового входа.

Автоматическое применение миграций управляется `Database__MigrateOnStartup` (в `.env` Docker Compose — `DATABASE_MIGRATE_ON_STARTUP`). В Docker Compose по умолчанию оно включено; без Docker при отсутствии настройки включено только в Development. Если автомиграции отключены, применить миграции отдельно перед запуском новой версии. Обновить настройки мастера и фронтенд под контракт телефона и `requiresContactConfirmation` вместо `requiresEmailConfirmation`.

### Уведомления о статусе

«Заявка принята» отправляется после успешного подтверждения кода, когда транзакция с аккаунтом, заявкой и погашенным кодом уже зафиксирована. При создании заявки авторизованным клиентом сообщение отправляется после сохранения заказа. Повторный вход и повторное подтверждение погашенного кода не отправляют его повторно.

После смены статуса мастером отправляются «В ремонте», «Готов к выдаче» и «Заявка отменена». При `Completed` и автоматической отмене неподтверждённой заявки сообщения и записи доставки не создаются. Автоматическая отмена сохраняет историю статуса напрямую; у такой заявки нет подтверждённого владельца. Уведомление об отмене отправляется только для подтверждённой заявки клиента.

`StatusNotificationService` определяет получателя по сохранённой привязке VK аккаунта владельца (`client_id`). Телефон из формы не используется для маршрутизации: изменение контактного телефона заявки не направит сообщение другому пользователю. Выбор канала и резервная доставка отсутствуют.

`notification_log` содержит `channel = Vk`, `recipient` — фактический VK ID, текст и заголовок сообщения, результат `Sent`/`Failed` и число попыток. Без привязки сохраняется `Failed`, пустой получатель и ноль попыток. Миграция `AddNotificationChannel` добавляет поле канала. Если схема уже соответствует остальным миграциям, для этого изменения достаточно применить новую миграцию.

По умолчанию выполняются до трёх попыток, пауза перед повтором растёт вдвое (`Notifications:RetryDelay`). Повторы выполняются в рамках текущего запроса; отдельной очереди повторной доставки после перезапуска нет. Исчерпание попыток оставляет `Failed` в журнале. Ошибки доставки, поиска получателя и записи журнала не откатывают сохранённую заявку или смену статуса; подтверждение кода по-прежнему выдаёт JWT. Отмена самого запроса прекращает отправку и повторы.

Утверждённые тексты находятся в `NotificationMessages`: код подставляется в сообщение подтверждения, постоянный номер заявки — в уведомления `Created`, `InProgress`, `Ready` и ручной `Cancelled`. Заголовки статусов русские, GUID и комментарии мастера в сообщения не включаются. Переменная выбора канала и текст его смены не используются.

### Проверки

Автоматические тесты используют HTTP-моки и не обращаются к реальному VK. Для проверки постоянного хранения задать `PROPHONE_TEST_POSTGRES_CONNECTION` — подключение PostgreSQL с правом создания БД — и выполнить `dotnet test server/ProPhoneService.Server.slnx`. Каждый интеграционный тест создаёт и удаляет отдельную БД `messenger_test_*`; рабочие таблицы не изменяются.

Для ручной проверки создать свежую заявку с телефоном, открыть ссылку из ответа, отправить сообщение сообществу, получить код и выполнить `/api/auth/confirm`. Проверить JWT, профиль `/api/me` и заказ в `/api/me/orders`. Повторить после перезапуска API и временного отключения сети. Для проверки через API без фронтенда достаточно Postman; VK должен разрешать сообщения пользователю.

Протоколы: [Bots Long Poll API](https://dev.vk.com/ru/api/bots-long-poll/getting-started), [официальная схема VK messages](https://github.com/VKCOM/vk-api-schema/tree/master/messages), [официальная схема VK groups](https://github.com/VKCOM/vk-api-schema/tree/master/groups).
