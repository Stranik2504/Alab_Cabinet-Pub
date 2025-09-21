## ALab Cabinet (оплата путёвок Adventures Lab)
Веб‑приложение на ASP.NET Core 8 MVC для генерации/приёма оплат по ссылкам (Tinkoff), авторизации через Telegram и ежедневного контроля просроченных платежей. Данные берутся из NocoDB. Логи пишутся через Serilog.

### Возможности
- Генерация и обработка платежей Tinkoff по ссылкам.
- Личный кабинет покупателя: `'/Cabinet/Index'`, ожидание оплаты: `'/Cabinet/WaitPayment'`, успешная оплата: `'/Paid/Index'`.
- Обработка кейса не найденного заказа: `'/NotFoundOrder/Index'`.
- Авторизация через Telegram: `'/TelegramAuth/Index'` (cookie‑аутентификация).
- Админ‑панель: `'/AdminView/Index'`.
- Ежедневная проверка просроченных платежей и кэш периодов.
- Логирование в `'/logs/log-<дата>.txt'`.

### Технологии
- .NET 8, ASP.NET Core MVC
- Serilog
- NocoDB
- Tinkoff Acquiring
- Cookie‑аутентификация, сессии (30 дней)
- Docker

### Настройка конфигурации
Создайте каталог `/files` рядом с исполняемым файлом приложения и добавьте конфиги.

Пример `/files/config.json`
```json
{
  "ConnectionNocoDbUrl": "https://noco.example.com",
  "TokenNocoDb": "nc_xxx",
  "NameDbNocoDb": "MainDB",
  "NameDataNocoDb": "DataDB",
  "TinkoffTerminalKey": "Txxx",
  "TinkoffPassword": "p@ssw0rd"
}
```

Пример `/files/tinkoff.json`
```json
{
  "NotificationUrl": "https://your-domain.example.com/api/tinkoff/callback",
  "SuccessUrl": "https://your-domain.example.com/Paid/Index",
  "FailUrl": "https://your-domain.example.com/NotFoundOrder/Index"
}
```

Кэш просрочек `/files/cache_periods.json`: массив объектов модели `ExpiresUserOrder`:
```json
[
  {
    "OrderId": "12345",
    "UserId": "999999",
    "ExpireAt": "2025-12-31T23:59:59Z",
    "Status": "Pending"
  }
]
```

Логи создаются в `/logs` автоматически.

### Локальный запуск
Соберите и запустите в Release, предварительно добавив файлы конфигурации.

```bash
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

### Запуск в Docker
```bash
docker build -t alab-cabinet .
docker compose up -d
```

### Безопасность и эксплуатация
- Установите `ASPNETCORE_ENVIRONMENT=Production` в продакшене.
- Храните ключи Tinkoff и токены NocoDB вне репозитория.
- Настройте HTTPS и обратный прокси при публикации.
- Следите за файлами логов в `'/logs'` (ротация по дням).

### Лицензия
MIT License. См. файл LICENSE.
