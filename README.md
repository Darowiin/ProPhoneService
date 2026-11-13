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

```bash
cp .env.example .env        # при необходимости поправить пароль
docker compose up --build   # БД на 5432, API на 8080
```

API применяет миграции при старте в режиме разработки (`Database.Migrate()`), Connection string передаётся через env `ConnectionStrings__Default`

### Backend без Docker

Вне Development обязателен `Jwt__Key` (ключ подписи JWT, 32+ символа) - иначе приложение останавливается при старте; в Development при отсутствии используется dev-ключ

`MASTER_EMAIL` (env `Auth__MasterEmail`) - email мастера: при старте ему апсертится клиент и аккаунт с ролью Master, вход по коду на этот адрес; пусто - мастер не заводится


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

## Схема БД

Полная ERD: [`docs/ErdDiagram.png`](docs/ErdDiagram.png)

| Таблица                | Назначение |
|------------------------|------------|
| `client`               | клиенты (uuid PK, name, phone, email, created_at) |
| `device`               | техника (type, manufacturer, model, serial_number) |
| `repair_order`         | заказ на ремонт (FK client_id - null до подтверждения email, device_id; status, total_price, problem_description, contact_email/contact_phone, preferred_visit_at, created_at) |
| `repair_status_history`| история смены статусов заказа (status, changed_at, comment, changed_by - аккаунт мастера) |
| `repair_order_service` | связка заказ↔услуга (FK repair_order_id, service_id; price) |
| `service`              | прайс-лист услуг (name, description, price, is_active, updated_at, updated_by) |
| `account`              | аккаунт входа (FK client_id 1:1; role - Client/Master, created_at) |
| `auth_code`            | одноразовые коды входа на email (code, expires_at, failed_attempts, locked_until) |
| `notification_log`     | лог уведомлений (FK repair_order_id; recipient, subject, message, status, attempts, created_at) |

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
PendingContact → Created → InDiagnostic → Agreed → InProgress → Ready → Completed; отмена (Cancelled) из Created/Agreed/InProgress
```

Гостевая заявка создаётся в `PendingContact` (клиента и аккаунта ещё нет, `ClientId` пуст) и активируется кодом на свой email: при подтверждении создаются клиент и аккаунт, заявка привязывается и переходит в `Created`; до этого мастер её не видит

Каждая смена пишет запись в `repair_status_history` одним батчем. Запрещённый переход -> 409 Conflict. Конфликт параллельных изменений (токен конкурентности `xmin`) ловится в репозитории как `ConcurrencyConflictException` и тоже отдаётся как 409

## Эндпоинты API

| Метод | Путь | Доступ | Описание |
|-------|------|--------|----------|
| GET | `/api/services` | публично | прайс-лист активных услуг (HybridCache, 5 мин) |
| POST/PUT/DELETE | `/api/services...` | Master | правка прайса (скрытие - `is_active=false`) |
| GET | `/api/orders?status=&visitFrom=&visitTo=&page=` | Master | страница заявок панели мастера (фильтр по статусу и желаемой дате визита, пагинация; неподтверждённые не видны) |
| GET | `/api/orders/{id}` | Client (свой) / Master | карточка заказа: клиент, устройство, услуги, тайм-лайн статусов; чужой или неподтверждённый заказ - 404 |
| POST | `/api/orders` | публично | создание заявки: без токена - `PendingContact` + код на email, с токеном - сразу `Created`; описание проблемы до 2000 символов; 201 + номер |
| GET | `/api/me/orders?status=&page=` | Client | заказы клиента из клейма токена (новые сверху, пагинация; неподтверждённые не попадают) |
| GET | `/api/me/orders/{id}` | Client | карточка своего заказа: устройство, услуги, стоимость, тайм-лайн; чужой - 404 |
| GET | `/api/master/stats?period=Month\Quarter\All` | Master | статистика: завершённые ремонты по типам устройств и сумма (по моменту перехода в `Completed`) |
| PATCH | `/api/orders/{id}/status` | Master | смена статуса по машине состояний (409 при запрещённом переходе/конфликте) |
| POST | `/api/auth/request-code` | публично | код входа на email (INotifier, демо - лог; единственный канал в спринте 2) |
| POST | `/api/auth/confirm` | публично | подтверждение кода: вход существующего аккаунта; аккаунт и клиент создаются только при подтверждении заявки, иначе 400; JWT + refresh в HttpOnly cookie |
| POST | `/api/auth/refresh` | cookie | ротация refresh-токена (погашенный старый отклоняется) |

## Уведомления (Telegram/VK)

Доставка сообщений и привязка получателей работают через `INotifier`. `MessengerNotifier` выбирает отправителя по каналу: `TelegramBotNotifier` для Telegram или `VkNotifier` для VK. Отправка без настроек или при отказе провайдера завершается `MessengerUnavailableException`; API отвечает 503 с `offlineOrderAvailable: true` и предложением оформить заказ в мастерской. Коды и тексты сообщений в логи не выводятся.

Настройки берутся из `Messengers` и env (плейсхолдеры — `.env.example`):

- `Messengers__Telegram__Token`, `Messengers__Telegram__BotUsername`, `Messengers__Telegram__UpdateMode`, `Messengers__Telegram__WebhookSecret`
- `Messengers__Vk__Token`, `Messengers__Vk__GroupId`, `Messengers__Vk__WebhookSecret`, `Messengers__Vk__ConfirmationCode`, `Messengers__Vk__ApiVersion`

Docker Compose сопоставляет им переменные `TELEGRAM_BOT_TOKEN`, `TELEGRAM_BOT_USERNAME`, `TELEGRAM_UPDATE_MODE`, `TELEGRAM_WEBHOOK_SECRET`, `VK_GROUP_TOKEN`, `VK_GROUP_ID`, `VK_WEBHOOK_SECRET`, `VK_CONFIRMATION_CODE`, `VK_API_VERSION`. В Production обязательны полные настройки обоих каналов; в Development приложение стартует без них, но отправка возвращает ошибку.

### API привязки

1. После создания гостевой заявки вызвать `POST /api/messengers/orders/{orderId}/link` с телом `{"channel":"Telegram"}` или `{"channel":"Vk"}`. Телефон берётся из заявки; ссылка создаётся только для `PendingContact` и истекает не позднее 5 минут с момента создания заявки
2. Для существующего аккаунта использовать `POST /api/messengers/me/link` с JWT и таким же телом. Телефон берётся из профиля аккаунта; произвольный accountId передать нельзя
3. Ответ содержит `channel`, `url`, `expiresAtUtc`. URL — одноразовый секрет, его нельзя публиковать или логировать. Он открывает Telegram с параметром `start` или диалог сообщества VK с `ref`
4. Отправитель команды/сообщения из проверенного события сохраняется как получатель. После успешной привязки Telegram отправляет сообщение с подтверждением. Привязка связывает телефон, указанный в заявке/профиле, с мессенджером; это не SMS-проверка владения SIM-картой

Хэши токенов и привязки хранятся в `messenger_binding`; миграция `AddMessengerBindings` добавляет таблицу, индексы и внешние ключи. Для выдачи ссылок действует лимит 10 запросов в минуту на IP. Повторный callback того же отправителя безопасен; чужой отправитель не может использовать погашенную ссылку. Смена получателя уже привязанного телефона требует авторизованного аккаунта.

### Подключение ботов

Telegram поддерживает два режима получения событий. В обоих режимах нужны токен и имя бота из BotFather; обрабатывается `/start <token>` только из личного чата.

**Polling — без домена и входящего HTTPS:**

1. В `.env` задать `TELEGRAM_UPDATE_MODE=Polling`, `TELEGRAM_BOT_TOKEN` и `TELEGRAM_BOT_USERNAME`. `TELEGRAM_WEBHOOK_SECRET` в этом режиме не требуется
2. Пересоздать API: `docker compose up -d --build --force-recreate api`
3. Проверить `docker compose logs -f api`: должно появиться сообщение о запуске long polling
4. Создать новую ссылку привязки через API, открыть её и нажать Start до истечения срока действия

Приложение само отключает прежний webhook через `deleteWebhook`, сохраняя ожидающие события. Нужен исходящий HTTPS-доступ к `api.telegram.org`; входящие подключения от Telegram не нужны. Запускать polling следует только в одном экземпляре API на токен бота, включая локальные запуски.

События обрабатываются последовательно. Следующий `getUpdates` подтверждает только успешно обработанные события; при ошибке запрос повторяется с прежним offset. После перезапуска возможна повторная доставка ещё не подтверждённого события, поэтому повторная привязка того же получателя безопасна. При повторной доставке сообщение об успешной привязке может прийти повторно. Токен бота, payload и секрет ссылки в логи не выводятся.

**Webhook — публичный HTTPS:**

Задать `TELEGRAM_UPDATE_MODE=Webhook` (режим по умолчанию) и `TELEGRAM_WEBHOOK_SECRET`. После пересоздания API зарегистрировать публичный HTTPS URL `/api/messengers/telegram/webhook` через `setWebhook` с тем же `secret_token`, разрешив обновления `message`. Регистрацию выполняет владелец бота; приложение не регистрирует webhook автоматически. В режиме Polling этот endpoint возвращает 404.

**VK:** включить сообщения сообщества, получить токен с доступом к сообщениям, настроить Callback API на публичный HTTPS URL `/api/messengers/vk/webhook`, указать секрет, строку подтверждения и событие `message_new`. API проверяет `group_id`; принимаются только личные сообщения, где `from_id == peer_id`. Настройку Callback API выполняет владелец сообщества.

Доставка использует `INotifier.SendAsync(channel, recipientId, text, cancellationToken)`. Метод `SendAsync(target, subject, text, cancellationToken)` принимает телефон, находит актуальную привязку и отправляет сообщение в её канал. Email как получатель не поддерживается.

Привязка бота сохраняет получателя, но не активирует заявку и не выдаёт JWT. Действующие контракты заявки и авторизации принимают email; отправка кода на такой контакт возвращает 503, поскольку доставка доступна только через привязанный мессенджер.

Тесты провайдеров используют HTTP-моки. Для регрессии атомарной привязки на PostgreSQL задать `PROPHONE_TEST_POSTGRES_CONNECTION` — подключение с правом создания БД — и выполнить `dotnet test server/ProPhoneService.Server.slnx`. Каждый интеграционный тест создаёт и удаляет отдельную БД `messenger_test_*`, включая применение миграций; рабочие таблицы приложения не изменяются.

Протоколы: [Telegram Bot API](https://core.telegram.org/bots/api), [Telegram deep linking](https://core.telegram.org/bots/features#deep-linking), [официальная схема VK messages](https://github.com/VKCOM/vk-api-schema/tree/master/messages), [официальная схема VK callbacks](https://github.com/VKCOM/vk-api-schema/tree/master/callback).

## Статистика

`GET /api/master/stats?period=` считает только завершённые ремонты: дата события - момент перехода заказа в `Completed` (`repair_status_history.changed_at`), период отсчитывается с начала месяца (`Month`, по умолчанию), с начала квартала (`Quarter`) или без нижней границы (`All`)

## Роли и доступ

В access-токене есть клеймы `sub` (аккаунт), `client_id` и `role` (`Client`/`Master`)

- **Client** - личный кабинет (`/api/me/orders`) и только собственные заказы: фильтр по `client_id` из клейма, чужой идентификатор неотличим от несуществующего (404), чтобы не раскрывать данные
- Отдельной регистрации нет: аккаунт создаётся только при подтверждении email заявки (`POST /api/orders` → код → `POST /api/auth/confirm`); дальше это обычный вход по коду, а для неизвестного email без заявки `confirm` отдаёт 400 «Аккаунт не найден»
- **Master** - панель заказов, смена статусов и правка прайса; публичный прайс читается без токена
- Мастер-аккаунт: апсерт при старте по `Auth__MasterEmail` (роль Master, вход по коду из лога `INotifier` в демо)
- Без токена защищённый эндпоинт - 401, с токеном без нужной роли - 403
- Поля аудита "кто изменил" (`repair_status_history.changed_by`, `service.updated_by`) заполняет `AuditInterceptor` из `sub` текущего запроса

Ошибки валидации возвращаются как `ValidationProblemDetails` со списком полей; при создании заявки обязательно должен быть телефон или почта, дата визита проверяется на рабочие часы и "не в прошлом"

## CI

`.github/workflows/ci.yml` - конфигурация CI/CD, запускается на каждый push:

- **backend**: `dotnet restore` → `build` → `test`
- **client**: `npm ci` → `lint`
