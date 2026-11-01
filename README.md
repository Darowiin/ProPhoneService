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

### Полный стек в Docker (PostgreSQL + API + фронт)

Требуется установленный Docker Desktop (на Windows — с бэкендом WSL 2). Проверка: `docker version` и `docker compose version`.

Фронтенд и рабочий API сейчас лежат на разных ветках, поэтому сначала объедините их в одну:

```bash
git checkout feature/frontend-core
git fetch https://github.com/Darowiin/ProPhoneService.git feat/backend-foundation
git merge FETCH_HEAD        # backend: контроллеры, Application-слои, свой Dockerfile
```

Затем поднимите всё:

```bash
cp .env.example .env        # при необходимости поправить пароль
docker compose up --build   # db 5432, api 8080, web 8081
```

| Адрес | Что это |
|---|---|
| http://localhost:8081 | фронтенд (nginx, статика из `client/build`) |
| http://localhost:8081/api/services | тот же origin, nginx проксирует в `api:8080` |
| http://localhost:8080/health | health-check API |
| localhost:5432 | PostgreSQL (`prophoneservice`) |

Как это устроено:

- `client/Dockerfile` — multi-stage: `node:22-alpine` собирает статику (`npm run build` → `build/`), `nginx:1.27-alpine` её раздаёт. `VITE_API_URL=/api` передаётся build-arg, потому что Vite подставляет `VITE_*` на этапе сборки.
- `client/nginx.conf` — SPA-фолбэк `try_files $uri $uri/ /index.html` (adapter-static кладёт fallback в `index.html`) и `location /api/ → http://api:8080`. CORS не нужен: браузер ходит на тот же origin.
- `docker-compose.yml` — сервис `web` собирается из `./client` и зависит от `api`.
- API в режиме `Development` сам применяет миграции и создаёт БД при старте, поэтому прайс-лист засеивается автоматически.

Полезные команды:

```bash
docker compose logs -f web      # логи nginx
docker compose logs -f api      # логи API + миграции
docker compose up --build web   # пересобрать только фронт после правок
docker compose down             # остановить
docker compose down -v          # остановить и стереть данные PostgreSQL
```

### Только БД и API в Docker, фронт локально

Быстрый путь, если фронт удобнее править с HMR:

```bash
cp .env.example .env
docker compose up --build db api    # API на 8080
```

```bash
cd client && npm ci && cp .env.example .env && npm run dev   # 5173, прокси /api → 8080
```

Вне Development обязателен `Jwt__Key` (ключ подписи JWT, 32+ символа) - иначе приложение останавливается при старте; в Development при отсутствии используется dev-ключ

`MASTER_EMAIL` (env `Auth__MasterEmail`) - email мастера: при старте ему апсертится клиент и аккаунт с ролью Master, вход по коду на этот адрес; пусто - мастер не заводится

`RESEND_API_KEY` (env `Email__Password`) + `EMAIL_FROM` (env `Email__FromAddress`) - отправка писем через Resend SMTP; вне Development эти настройки обязательны, в Development без них коды пишутся в лог

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

To connect the API from `Darowiin/ProPhoneService`, branch `feat/backend-foundation`, copy `client/.env.example` to `client/.env` and start that backend on port 8080. Vite proxies `/api` to `VITE_API_PROXY_TARGET`, so local development does not require CORS.

```bash
cd client
copy .env.example .env   # Windows PowerShell; use cp on POSIX
npm run dev
```

The backend branch currently exposes no client, service or price data in its order list DTO, so the master panel shows only the fields it returns and renders placeholders for monthly revenue and clients. For production, host the static frontend and proxy `/api` to the backend on the same origin; the backend branch does not configure CORS.

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
| `review`               | отзывы клиентов (FK client_id; rating, text, created_at) |

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
| POST | `/api/auth/confirm` | публично | подтверждение кода: аккаунт + клиент, JWT + refresh в HttpOnly cookie |
| POST | `/api/auth/refresh` | cookie | ротация refresh-токена (погашенный старый отклоняется) |

## Уведомления (email)

Отправка сообщений идёт через `INotifier`; реализация выбирается по настройкам секции `Email`:

- **Resend SMTP** (`SmtpEmailNotifier`, MailKit) - когда заданы `Email__Password` (API-ключ) и `Email__FromAddress`; хост/порт/пользователь по умолчанию `smtp.resend.com` / `465` / `resend` (465 - неявный TLS, 587/2465/2587 - STARTTLS)
- **лог-заглушка** (`LoggingNotifier`) - когда провайдер не настроен и окружение Development; вне Development приложение не стартует без настроек

Отправителя (`Email__FromAddress`) нужно подтвердить в Resend; для тестов подходит `onboarding@resend.dev` (письма только на адрес владельца ключа). `Email__UseTls=false` - только для локального SMTP без шифрования (MailHog)

## Статистика

`GET /api/master/stats?period=` считает только завершённые ремонты: дата события - момент перехода заказа в `Completed` (`repair_status_history.changed_at`), период отсчитывается с начала месяца (`Month`, по умолчанию), с начала квартала (`Quarter`) или без нижней границы (`All`)

## Роли и доступ

В access-токене есть клеймы `sub` (аккаунт), `client_id` и `role` (`Client`/`Master`)

- **Client** - личный кабинет (`/api/me/orders`) и только собственные заказы: фильтр по `client_id` из клейма, чужой идентификатор неотличим от несуществующего (404), чтобы не раскрывать данные
- **Master** - панель заказов, смена статусов и правка прайса; публичный прайс читается без токена
- Мастер-аккаунт: апсерт при старте по `Auth__MasterEmail` (роль Master, вход по коду из лога `INotifier` в демо)
- Без токена защищённый эндпоинт - 401, с токеном без нужной роли - 403
- Поля аудита "кто изменил" (`repair_status_history.changed_by`, `service.updated_by`) заполняет `AuditInterceptor` из `sub` текущего запроса

Ошибки валидации возвращаются как `ValidationProblemDetails` со списком полей; при создании заявки обязательно должен быть телефон или почта, дата визита проверяется на рабочие часы и "не в прошлом"

## CI

`.github/workflows/ci.yml` - конфигурация CI/CD, запускается на каждый push:

- **backend**: `dotnet restore` → `build` → `test`
- **client**: `npm ci` → `lint`
