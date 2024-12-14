# Telegram Water Bot

Telegram бот для заказа бутилированной воды, написанный на C#.

## Функциональность

- Просмотр каталога продуктов с фотографиями
- Оформление заказов
- История заказов
- Повторение предыдущих заказов
- Выбор даты и времени доставки
- Уведомления администратора о новых заказах

## Технологии

- .NET Core
- PostgreSQL
- Redis
- Telegram Bot API

## Установка и запуск

1. Клонируйте репозиторий
2. Создайте файл `appsettings.json` со следующей структурой:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=telegram_water;Username=your_username;Password=your_password",
    "Redis": "localhost:6379"
  },
  "BotConfiguration": {
    "BotToken": "YOUR_BOT_TOKEN",
    "AdminChatId": YOUR_ADMIN_CHAT_ID
  }
}
```
3. Установите зависимости: `dotnet restore`
4. Запустите приложение: `dotnet run`

## База данных

Для инициализации базы данных используйте скрипты:
- `update_orders_table.sql` - создание таблицы заказов
- `update_product_images.sql` - обновление изображений продуктов
