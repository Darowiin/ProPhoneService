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
5. Обработчик принимает только личное сообщение нужного сообщества, где положительные `from_id` и `peer_id` совпадают. После сохранения привязки отправляется утверждённое сообщение «Мессенджер успешно привязан!»

Хэши секретов и получатели хранятся в `messenger_binding`. Действует лимит выдачи ссылок 10 запросов в минуту на IP. Чужой отправитель не может погасить уже использованную ссылку. Новая гостевая заявка не позволяет заменить существующего получателя телефона. Старые привязки неподдерживаемых каналов не участвуют в поиске и доставке; заявки и аккаунты сохраняются.

Long Poll обрабатывает события последовательно. Позиция `ts` продвигается после успешной обработки всей пачки; при ошибке повторяется текущее событие, уже обработанные элементы пачки пропускаются. `failed=1` обновляет позицию, `failed=2` обновляет ключ с сохранением позиции, `failed=3` открывает новую сессию. При ошибках транспорта выполняется повтор через 5 секунд. Токены, ключи Long Poll, payload и текст исключений в логи не выводятся; доступны этап, тип ошибки, безопасный код и стек вызовов.

Позиция Long Poll и текущая пачка хранятся в памяти: после перезапуска открывается новая сессия, гарантии воспроизведения событий за время простоя нет. Если привязка не завершилась, следует получить свежую ссылку и повторить действие. Повторная привязка того же получателя безопасна, но подтверждающее сообщение при повторной доставке события может прийти повторно.

`INotifier.SendAsync(channel, recipientId, text, cancellationToken)` поддерживает только VK. Совместимый метод `SendAsync(target, subject, text, cancellationToken)` принимает телефон, находит привязку VK и отправляет сообщение; email не поддерживается. При отказе провайдера выбрасывается `MessengerUnavailableException`, API отвечает 503 с `offlineOrderAvailable: true` и предложением оформить заказ в мастерской.

Привязка сохраняет получателя, но сама не активирует заявку и не выдаёт JWT. Действующие контракты заявки и авторизации пока принимают email; отправка кода на такой контакт возвращает ошибку. Переход входа и подтверждения заявки на телефон и VK выполняется отдельно.

### Проверки

Автоматические тесты используют HTTP-моки и не обращаются к реальному VK. Для проверки постоянного хранения задать `PROPHONE_TEST_POSTGRES_CONNECTION` — подключение PostgreSQL с правом создания БД — и выполнить `dotnet test server/ProPhoneService.Server.slnx`. Каждый интеграционный тест создаёт и удаляет отдельную БД `messenger_test_*`; рабочие таблицы не изменяются.

Для ручной проверки создать свежую заявку с телефоном, получить ссылку, отправить сообщение сообществу по ссылке и убедиться, что пришло подтверждение. Повторить после перезапуска API и временного отключения сети. Для проверки через API без фронтенда достаточно Postman; VK должен разрешать сообщения пользователю.

Протоколы: [Bots Long Poll API](https://dev.vk.com/ru/api/bots-long-poll/getting-started), [официальная схема VK messages](https://github.com/VKCOM/vk-api-schema/tree/master/messages), [официальная схема VK groups](https://github.com/VKCOM/vk-api-schema/tree/master/groups).
