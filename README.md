# ProPhoneService

Сервис записи на ремонт телефонов: витрина, заказы на ремонт, личный кабинет клиента и панель мастера.

Монорепозиторий:

```
.
├── client/   # фронтенд (Svelte + TypeScript, Vite) — пока только конфиг и линтер
├── server/   # бэкенд ASP.NET Core Web API + xUnit-тесты
├── docs/     # продуктовая документация, ERD
└── .github/  # CI (GitHub Actions)
```

## Стек

| Слой     | Технологии |
|----------|------------|
| Фронт    | Svelte 5 + TypeScript, Vite, ESLint |
| Бэк      | ASP.NET Core (Web API), xUnit |
| БД       | PostgreSQL (планируется, схема ниже) |
| CI       | GitHub Actions |

## Запуск

### Backend

```bash
cd server
dotnet restore
dotnet build
dotnet test          # прогон тестов
dotnet run --project ProPhoneService.Api
```

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

## CI

`.github/workflows/ci.yml` — на каждый PR и push в `main`:

- **backend**: `dotnet restore` → `build` → `test`
- **client**: `npm ci` → `lint`
