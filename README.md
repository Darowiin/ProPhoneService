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

API применяет миграции при старте (`Database.Migrate()`), Connection string передаётся через env `ConnectionStrings__Default`

### Backend без Docker

```bash
cd server
dotnet restore
dotnet build
dotnet test          # прогон тестов (35)
dotnet run --project ProPhoneService.Api
```

Connection string для локального запуска - в `server/ProPhoneService.Api/appsettings.Development.json`, БД при этом всё равно нужна - например, `docker compose up db`

Health-check: `GET /health` → `Healthy`.

### Frontend

```bash
cd client
npm ci
npm run lint
```

## Схема БД

Полная ERD: [`docs/ErdDiagram.png`](docs/ErdDiagram.png)

| Таблица                | Назначение |
|------------------------|------------|
| `client`               | клиенты (uuid PK, name, phone, email, created_at) |
| `device`               | техника (type, manufacturer, model, serial_number) |
| `repair_order`         | заказ на ремонт (FK client_id, device_id; status, total_price, created_at) |
| `repair_status_history`| история смены статусов заказа (status, changed_at, comment) |
| `repair_order_service` | связка заказ↔услуга (FK repair_order_id, service_id; price) |
| `service`              | прайс-лист услуг (name, description, price, is_active) |
| `review`               | отзывы клиентов (FK client_id; rating, text, created_at) |

## Архитектура server

```
server/
├── ProPhoneService.Domain.Shared/   # enum-ы, машина состояний (без зависимостей)
├── ProPhoneService.Domain/          # сущности, интерфейсы IRepository
├── ProPhoneService.Infrastructure.EfCore/  # DbContext, конфигурации, миграции, реализации репозиториев
├── ProPhoneService.Api/             # эндпоинты, DI
└── ProPhoneService.Api.Tests/      # xUnit
```

Репозитории - generic `IRepository<T>` + специализированные, scoped lifetime, batch-сохранение через `SaveChangesAsync`

## Машина состояний заказа

Смена статуса - только через `RepairOrder.ChangeStatus()`, Карта переходов - `RepairStatusTransitions` в `Domain.Shared`

```
Матрица переходов: Created → Diagnostics → Agreed → InProgress → Ready → Completed; отмена (Cancelled) из Created/Agreed/InProgress
```

Каждая смена пишет запись в `repair_status_history`, Запрещённый переход -> `InvalidOperationException` (на уровне API будет 409)

## CI

`.github/workflows/ci.yml` - конфигурация CI/CD, запускается на каждый PR и push в `main`:

- **backend**: `dotnet restore` → `build` → `test`
- **client**: `npm ci` → `lint`
