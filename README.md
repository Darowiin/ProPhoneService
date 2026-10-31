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

Вне Development обязателен `Jwt__Key` (ключ подписи JWT, 32+ символа) - иначе приложение останавливается при старте; в Development при отсутствии используется dev-ключ

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
npm run lint
```

## Схема БД

Полная ERD: [`docs/ErdDiagram.png`](docs/ErdDiagram.png)

| Таблица                | Назначение |
|------------------------|------------|
| `client`               | клиенты (uuid PK, name, phone, email, created_at) |
| `device`               | техника (type, manufacturer, model, serial_number) |
| `repair_order`         | заказ на ремонт (FK client_id, device_id; status, total_price, preferred_visit_at, created_at) |
| `repair_status_history`| история смены статусов заказа (status, changed_at, comment) |
| `repair_order_service` | связка заказ↔услуга (FK repair_order_id, service_id; price) |
| `service`              | прайс-лист услуг (name, description, price, is_active) |
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

## Машина состояний заказа

Смена статуса - только через `RepairOrder.ChangeStatus()`, карта переходов - `RepairStatusTransitions` в `Domain.Shared`
Заказ создаётся фабрикой `RepairOrder.Create(...)`: до инициализации `Status == null`, `ChangeStatus` выбрасывает исключение

```
Created → InDiagnostic → Agreed → InProgress → Ready → Completed; отмена (Cancelled) из Created/Agreed/InProgress
```

Каждая смена пишет запись в `repair_status_history` одним батчем. Запрещённый переход -> 409 Conflict. Конфликт параллельных изменений (токен конкурентности `xmin`) ловится в репозитории как `ConcurrencyConflictException` и тоже отдаётся как 409

## Эндпоинты API

| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/services` | прайс-лист активных услуг (HybridCache, 5 мин) |
| GET | `/api/orders?status=&visitFrom=&visitTo=&page=` | страница заявок панели мастера (фильтр по статусу и желаемой дате визита, пагинация) |
| GET | `/api/orders/{id}` | карточка заказа: клиент, устройство, услуги, тайм-лайн статусов |
| POST | `/api/orders` | создание заявки (клиент find-or-create по телефону/email, 201 + номер) |
| PATCH | `/api/orders/{id}/status` | смена статуса по машине состояний (409 при запрещённом переходе/конфликте) |
| POST | `/api/auth/request-code` | код входа на email (INotifier, демо - лог; единственный канал в спринте 2) |
| POST | `/api/auth/confirm` | подтверждение кода: аккаунт + клиент, JWT + refresh в HttpOnly cookie |
| POST | `/api/auth/refresh` | ротация refresh-токена (погашенный старый отклоняется) |

Ошибки валидации возвращаются как `ValidationProblemDetails` со списком полей; при создании заявки обязательно должен быть телефон или почта, дата визита проверяется на рабочие часы и "не в прошлом"

## CI

`.github/workflows/ci.yml` - конфигурация CI/CD, запускается на каждый push:

- **backend**: `dotnet restore` → `build` → `test`
- **client**: `npm ci` → `lint`
