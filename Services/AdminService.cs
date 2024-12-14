using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramWaterBot.Models;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace TelegramWaterBot.Services
{
    public class AdminService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly DatabaseService _dbService;
        private readonly CacheService _cacheService;
        private readonly long _adminChatId;

        public AdminService(
            ITelegramBotClient botClient,
            DatabaseService dbService,
            CacheService cacheService,
            long adminChatId)
        {
            _botClient = botClient;
            _dbService = dbService;
            _cacheService = cacheService;
            _adminChatId = adminChatId;
        }

        public async Task HandleAdminCommand(Message message, CancellationToken cancellationToken)
        {
            Console.WriteLine($"HandleAdminCommand called. ChatId: {message.Chat.Id}, AdminChatId: {_adminChatId}");

            if (message.Chat.Id != _adminChatId)
            {
                Console.WriteLine("Access denied: user is not admin");
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "У вас нет прав для выполнения этой команды.",
                    cancellationToken: cancellationToken);
                return;
            }

            Console.WriteLine("Access granted: showing admin menu");
            var command = message.Text?.ToLower().Split(' ')[0];
            switch (command)
            {
                case "/admin":
                    await ShowAdminMenu(message.Chat.Id, cancellationToken);
                    break;
                case "/addproduct":
                    await StartAddProduct(message.Chat.Id, cancellationToken);
                    break;
                case "/updatestock":
                    await ShowUpdateStockMenu(message.Chat.Id, cancellationToken);
                    break;
                case "/toggleproduct":
                    await ShowToggleProductMenu(message.Chat.Id, cancellationToken);
                    break;
            }
        }

        private async Task ShowAdminMenu(long chatId, CancellationToken cancellationToken)
        {
            Console.WriteLine($"ShowAdminMenu called for ChatId: {chatId}");
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить товар", "admin_add_product") },
                new[] { InlineKeyboardButton.WithCallbackData("📦 Обновить остаток", "admin_update_stock") },
                new[] { InlineKeyboardButton.WithCallbackData("🔄 Включить/Выключить товар", "admin_toggle_product") },
                new[] { InlineKeyboardButton.WithCallbackData("« Назад", "menu") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🔧 Меню администратора:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }

        private async Task StartAddProduct(long chatId, CancellationToken cancellationToken)
        {
            var userState = await _cacheService.GetUserState(chatId);
            userState.State = "AdminAddingProductName";
            await _cacheService.SetUserState(chatId, userState);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Введите название нового товара:",
                cancellationToken: cancellationToken);
        }

        public async Task HandleAdminState(Message message, string state, CancellationToken cancellationToken)
        {
            var userState = await _cacheService.GetUserState(message.Chat.Id);
            decimal price = 0;
            string photoId = "";

            switch (state)
            {
                case "AdminAddingProductName":
                    userState.State = "AdminAddingProductDescription";
                    userState.TempData["ProductName"] = message.Text ?? "";
                    await _cacheService.SetUserState(message.Chat.Id, userState);
                    
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введите описание товара:",
                        cancellationToken: cancellationToken);
                    break;

                case "AdminAddingProductDescription":
                    userState.State = "AdminAddingProductPrice";
                    userState.TempData["ProductDescription"] = message.Text ?? "";
                    await _cacheService.SetUserState(message.Chat.Id, userState);
                    
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введите цену товара (например, 299.99):",
                        cancellationToken: cancellationToken);
                    break;

                case "AdminAddingProductPrice":
                    if (!decimal.TryParse(message.Text, out price))
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Неверный формат цены. Пожалуйста, введите число (например, 299.99):",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    userState.State = "AdminAddingProductImage";
                    userState.TempData["ProductPrice"] = price.ToString();
                    await _cacheService.SetUserState(message.Chat.Id, userState);
                    
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Отправьте фотографию товара:",
                        cancellationToken: cancellationToken);
                    break;

                case "AdminAddingProductImage":
                    if (message.Photo == null || message.Photo.Length == 0)
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Пожалуйста, отправьте фотографию:",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    photoId = message.Photo[^1].FileId;
                    userState.State = "AdminAddingProductStock";
                    userState.TempData["ProductImage"] = photoId;
                    await _cacheService.SetUserState(message.Chat.Id, userState);
                    
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "Введите начальное количество товара на складе:",
                        cancellationToken: cancellationToken);
                    break;

                case "AdminAddingProductStock":
                    if (!int.TryParse(message.Text, out int stock) || stock < 0)
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: message.Chat.Id,
                            text: "Неверное количество. Пожалуйста, введите положительное целое число:",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    // Создаем новый продукт
                    var newProduct = new Product
                    {
                        Name = userState.TempData["ProductName"],
                        Description = userState.TempData["ProductDescription"],
                        Price = decimal.Parse(userState.TempData["ProductPrice"]),
                        ImageUrl = userState.TempData["ProductImage"],
                        StockQuantity = stock,
                        IsAvailable = true
                    };

                    await _dbService.CreateProduct(newProduct);
                    userState.State = "Start";
                    userState.TempData.Clear();
                    await _cacheService.SetUserState(message.Chat.Id, userState);

                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "✅ Товар успешно добавлен!",
                        cancellationToken: cancellationToken);
                    
                    await ShowAdminMenu(message.Chat.Id, cancellationToken);
                    break;
            }
        }

        private async Task ShowUpdateStockMenu(long chatId, CancellationToken cancellationToken)
        {
            var products = await _dbService.GetAllProducts();
            var buttons = new List<InlineKeyboardButton[]>();

            foreach (var product in products)
            {
                buttons.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{product.Name} (Остаток: {product.StockQuantity})",
                        $"update_stock_{product.Id}")
                });
            }

            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад", "admin_menu") });

            var keyboard = new InlineKeyboardMarkup(buttons);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите товар для обновления остатка:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }

        private async Task ShowToggleProductMenu(long chatId, CancellationToken cancellationToken)
        {
            var products = await _dbService.GetAllProducts();
            var buttons = new List<InlineKeyboardButton[]>();

            foreach (var product in products)
            {
                var status = product.IsAvailable ? "✅" : "❌";
                buttons.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"{status} {product.Name}",
                        $"toggle_product_{product.Id}")
                });
            }

            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("« Назад", "admin_menu") });

            var keyboard = new InlineKeyboardMarkup(buttons);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите товар для изменения доступности:",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }

        public async Task HandleAdminCallback(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message?.Chat.Id != _adminChatId)
            {
                await _botClient.AnswerCallbackQueryAsync(
                    callbackQueryId: callbackQuery.Id,
                    text: "У вас нет прав для выполнения этой команды.",
                    cancellationToken: cancellationToken);
                return;
            }

            switch (callbackQuery.Data)
            {
                case "admin_menu":
                    await ShowAdminMenu(callbackQuery.Message.Chat.Id, cancellationToken);
                    break;

                case "admin_add_product":
                    await StartAddProduct(callbackQuery.Message.Chat.Id, cancellationToken);
                    break;

                case "admin_update_stock":
                    await ShowUpdateStockMenu(callbackQuery.Message.Chat.Id, cancellationToken);
                    break;

                case "admin_toggle_product":
                    await ShowToggleProductMenu(callbackQuery.Message.Chat.Id, cancellationToken);
                    break;

                default:
                    if (callbackQuery.Data?.StartsWith("update_stock_") == true)
                    {
                        var productId = int.Parse(callbackQuery.Data.Replace("update_stock_", ""));
                        var userState = await _cacheService.GetUserState(callbackQuery.Message.Chat.Id);
                        userState.State = "AdminUpdatingStock";
                        userState.SelectedProductId = productId;
                        await _cacheService.SetUserState(callbackQuery.Message.Chat.Id, userState);

                        await _botClient.SendTextMessageAsync(
                            chatId: callbackQuery.Message.Chat.Id,
                            text: "Введите новое количество товара на складе:",
                            cancellationToken: cancellationToken);
                    }
                    else if (callbackQuery.Data?.StartsWith("toggle_product_") == true)
                    {
                        var productId = int.Parse(callbackQuery.Data.Replace("toggle_product_", ""));
                        var product = await _dbService.GetProduct(productId);
                        
                        if (product != null)
                        {
                            product.IsAvailable = !product.IsAvailable;
                            await _dbService.UpdateProduct(product);

                            var status = product.IsAvailable ? "доступен" : "недоступен";
                            await _botClient.SendTextMessageAsync(
                                chatId: callbackQuery.Message.Chat.Id,
                                text: $"Товар \"{product.Name}\" теперь {status}",
                                cancellationToken: cancellationToken);

                            await ShowToggleProductMenu(callbackQuery.Message.Chat.Id, cancellationToken);
                        }
                    }
                    break;
            }

            await _botClient.AnswerCallbackQueryAsync(
                callbackQueryId: callbackQuery.Id,
                cancellationToken: cancellationToken);
        }
    }
}
