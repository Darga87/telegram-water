using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Polling;
using TelegramWaterBot.Services;
using TelegramWaterBot.Models;

namespace TelegramWaterBot
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    // Add configuration
                    services.AddSingleton<IConfiguration>(context.Configuration);

                    // Add bot client
                    services.AddSingleton<ITelegramBotClient>(provider =>
                    {
                        var config = provider.GetService<IConfiguration>();
                        var token = config["BotConfiguration:BotToken"];
                        return new TelegramBotClient(token);
                    });

                    // Add services
                    services.AddSingleton<DatabaseService>();
                    services.AddSingleton<CacheService>();
                    services.AddHostedService<BotService>();
                })
                .Build();

            await host.RunAsync();
        }
    }

    public class BotService : IHostedService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IConfiguration _configuration;
        private readonly DatabaseService _dbService;
        private readonly CacheService _cacheService;

        public BotService(
            ITelegramBotClient botClient,
            IConfiguration configuration,
            DatabaseService dbService,
            CacheService cacheService)
        {
            _botClient = botClient;
            _configuration = configuration;
            _dbService = dbService;
            _cacheService = cacheService;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Установка команд бота
            var commands = new List<BotCommand>
            {
                new BotCommand { Command = "start", Description = "Начать работу с ботом" },
                new BotCommand { Command = "menu", Description = "Показать главное меню" },
                new BotCommand { Command = "products", Description = "Показать ассортимент" },
                new BotCommand { Command = "order", Description = "Сделать заказ" },
                new BotCommand { Command = "history", Description = "История заказов" }
            };
            await _botClient.SetMyCommandsAsync(commands, cancellationToken: cancellationToken);

            var receiverOptions = new ReceiverOptions { AllowedUpdates = { } };
            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                cancellationToken);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is Message message)
            {
                var chatId = message.Chat.Id;
                var userState = await _cacheService.GetUserState(chatId);

                try
                {
                    if (message.Text?.StartsWith("/") == true)
                    {
                        await HandleCommand(message, cancellationToken);
                        return;
                    }

                    switch (userState.State)
                    {
                        case "Start":
                            await HandleMainMenu(message, cancellationToken);
                            break;
                        case "SelectingProduct":
                            await HandleProductSelection(message, userState, cancellationToken);
                            break;
                        case "EnteringQuantity":
                            await HandleQuantityInput(message, userState, cancellationToken);
                            break;
                        case "AwaitingPhoneNumber":
                            if (message.Contact != null)
                            {
                                message.Text = message.Contact.PhoneNumber;
                            }
                            await HandlePhoneNumberInput(message, userState, cancellationToken);
                            break;
                        case "AwaitingAddress":
                            await HandleAddressInput(message, userState, cancellationToken);
                            break;
                        case "AwaitingDate":
                            await HandleDateTimeInput(message, userState, cancellationToken);
                            break;
                        default:
                            await HandleMainMenu(message, cancellationToken);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing update: {ex}");
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Произошла ошибка при обработке вашего запроса. Пожалуйста, попробуйте снова.",
                        cancellationToken: cancellationToken);
                }
            }
            else if (update.CallbackQuery is { } callbackQuery)
            {
                try
                {
                    await HandleCallbackQuery(callbackQuery, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing callback query: {ex}");
                    await botClient.AnswerCallbackQueryAsync(
                        callbackQueryId: callbackQuery.Id,
                        text: "Произошла ошибка. Пожалуйста, попробуйте снова.",
                        cancellationToken: cancellationToken);
                }
            }
        }

        private async Task HandleCallbackQuery(CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            var chatId = callbackQuery.Message.Chat.Id;
            var userState = await _cacheService.GetUserState(chatId);

            switch (callbackQuery.Data)
            {
                case "menu":
                    await ShowMainMenu(chatId, cancellationToken);
                    break;
                case "products":
                    await ShowProducts(chatId, cancellationToken);
                    break;
                case "order":
                    userState.State = "SelectingProduct";
                    await _cacheService.SetUserState(chatId, userState);
                    await StartOrder(chatId, cancellationToken);
                    break;
                case "history":
                    await ShowPastOrders(chatId, cancellationToken);
                    break;
                case "confirm_repeat":
                    if (userState.CurrentOrder != null)
                    {
                        // Проверяем, что дата и время доставки установлены и корректны
                        if (!userState.CurrentOrder.DeliveryDate.HasValue || !userState.CurrentOrder.DeliveryTime.HasValue)
                        {
                            await _botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Ошибка: не указаны дата или время доставки.",
                                cancellationToken: cancellationToken);
                            return;
                        }

                        var today = DateTime.Now.Date;
                        if (userState.CurrentOrder.DeliveryDate.Value < today)
                        {
                            await _botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Ошибка: дата доставки не может быть раньше текущей даты.",
                                cancellationToken: cancellationToken);
                            return;
                        }

                        var minTime = new TimeSpan(9, 0, 0); // 9:00
                        var maxTime = new TimeSpan(21, 0, 0); // 21:00
                        if (userState.CurrentOrder.DeliveryTime.Value < minTime || userState.CurrentOrder.DeliveryTime.Value > maxTime)
                        {
                            await _botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Ошибка: время доставки должно быть между 9:00 и 21:00.",
                                cancellationToken: cancellationToken);
                            return;
                        }

                        try
                        {
                            var orderId = await _dbService.CreateOrder(userState.CurrentOrder);
                            await NotifyAdmin(
                                _configuration.GetValue<long>("BotConfiguration:AdminChatId"),
                                userState.CurrentOrder,
                                orderId,
                                cancellationToken);

                            await _botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: $"✅ Заказ успешно оформлен!\n" +
                                      $"Номер заказа: {orderId}\n" +
                                      $"Дата доставки: {userState.CurrentOrder.DeliveryDate:dd.MM.yyyy}\n" +
                                      $"Время доставки: {userState.CurrentOrder.DeliveryTime:hh\\:mm}",
                                cancellationToken: cancellationToken);

                            // Сбрасываем состояние пользователя
                            await _cacheService.ClearUserState(chatId);

                            // Показываем главное меню
                            await ShowMainMenu(chatId, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error creating order: {ex}");
                            await _botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: "Произошла ошибка при создании заказа. Пожалуйста, попробуйте еще раз.",
                                cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "Ошибка: заказ не найден.",
                            cancellationToken: cancellationToken);
                    }
                    break;
                default:
                    if (callbackQuery.Data.StartsWith("order_"))
                    {
                        var productId = int.Parse(callbackQuery.Data.Replace("order_", ""));
                        userState.SelectedProductId = productId;
                        userState.State = "EnteringQuantity";
                        await _cacheService.SetUserState(chatId, userState);

                        var product = await _dbService.GetProduct(productId);
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Вы выбрали: {product.Name}\nВведите количество:",
                            cancellationToken: cancellationToken);
                    }
                    else if (callbackQuery.Data.StartsWith("repeat_"))
                    {
                        var orderId = int.Parse(callbackQuery.Data.Replace("repeat_", ""));
                        await RepeatOrder(chatId, orderId, cancellationToken);
                    }
                    break;
            }

            // Отвечаем на callback query
            await _botClient.AnswerCallbackQueryAsync(
                callbackQueryId: callbackQuery.Id,
                cancellationToken: cancellationToken);
        }

        private async Task HandleCommand(Message message, CancellationToken cancellationToken)
        {
            switch (message.Text.ToLower())
            {
                case "/start":
                    await HandleStartCommand(message, cancellationToken);
                    break;
                case "/menu":
                    await ShowMainMenu(message.Chat.Id, cancellationToken);
                    break;
                case "/products":
                    await ShowProducts(message.Chat.Id, cancellationToken);
                    break;
                case "/order":
                    var userState = await _cacheService.GetUserState(message.Chat.Id);
                    userState.State = "SelectingProduct";
                    await _cacheService.SetUserState(message.Chat.Id, userState);
                    await StartOrder(message.Chat.Id, cancellationToken);
                    break;
                case "/history":
                    await ShowPastOrders(message.Chat.Id, cancellationToken);
                    break;
            }
        }

        private async Task HandleStartCommand(Message message, CancellationToken cancellationToken)
        {
            var userState = new UserState { State = "Start" };
            await _cacheService.SetUserState(message.Chat.Id, userState);

            await ShowMainMenu(message.Chat.Id, cancellationToken);
        }

        private async Task ShowMainMenu(long chatId, CancellationToken cancellationToken)
        {
            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithCallbackData("Ассортимент", "products"),
                    InlineKeyboardButton.WithCallbackData("Заказать", "order")
                },
                new []
                {
                    InlineKeyboardButton.WithCallbackData("История заказов", "history")
                }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Добро пожаловать в магазин бутилированной воды! Выберите действие:",
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task ShowProducts(long chatId, CancellationToken cancellationToken)
        {
            var products = await _dbService.GetAllProducts();
            
            foreach (var product in products)
            {
                // Создаем клавиатуру для каждого продукта
                var inlineKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🛒 Заказать", $"order_{product.Id}") },
                });

                try
                {
                    // Формируем текст описания
                    var description = $"📦 {product.Name}\n" +
                                    $"📝 {product.Description}\n" +
                                    $"💰 Цена: {product.Price:C}";

                    // Отправляем фото по URL
                    if (!string.IsNullOrEmpty(product.ImageUrl))
                    {
                        try
                        {
                            var inputFile = InputFile.FromUri(product.ImageUrl);
                            await _botClient.SendPhotoAsync(
                                chatId: chatId,
                                photo: inputFile,
                                caption: description,
                                replyMarkup: inlineKeyboard,
                                cancellationToken: cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error sending photo for product {product.Id}: {ex.Message}");
                            // Если не удалось отправить фото, отправляем только текст
                            await _botClient.SendTextMessageAsync(
                                chatId: chatId,
                                text: description,
                                replyMarkup: inlineKeyboard,
                                parseMode: ParseMode.Html,
                                cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: description,
                            replyMarkup: inlineKeyboard,
                            parseMode: ParseMode.Html,
                            cancellationToken: cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error showing product {product.Id}: {ex.Message}");
                    continue; // Продолжаем с следующим продуктом в случае ошибки
                }
            }

            // Добавляем кнопку возврата в меню
            var menuKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("« Назад в меню", "menu") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите действие:",
                replyMarkup: menuKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task StartOrder(long chatId, CancellationToken cancellationToken)
        {
            var products = await _dbService.GetAllProducts();
            var productButtons = products.Select(p =>
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(
                        $"🛒 {p.Name} - {p.Price:C}",
                        $"order_{p.Id}")
                }).ToList();

            var inlineKeyboard = new InlineKeyboardMarkup(productButtons.Concat(new[] {
                new[] { InlineKeyboardButton.WithCallbackData("« Назад в меню", "menu") }
            }));

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "🛍 Выберите товар для заказа:",
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task HandleMainMenu(Message message, CancellationToken cancellationToken)
        {
            switch (message.Text)
            {
                case "Ассортимент":
                    await ShowProducts(message.Chat.Id, cancellationToken);
                    break;
                case "Заказать":
                    var userState = await _cacheService.GetUserState(message.Chat.Id);
                    userState.State = "SelectingProduct";
                    await _cacheService.SetUserState(message.Chat.Id, userState);
                    await StartOrder(message.Chat.Id, cancellationToken);
                    break;
                case "Ваши прошлые заказы":
                    await ShowPastOrders(message.Chat.Id, cancellationToken);
                    break;
                default:
                    await HandleStartCommand(message, cancellationToken);
                    break;
            }
        }

        private async Task HandleProductSelection(Message message, UserState userState, CancellationToken cancellationToken)
        {
            var products = await _dbService.GetAllProducts();
            var selectedProduct = products.FirstOrDefault(p => message.Text == $"Заказать {p.Name}");

            if (selectedProduct != null)
            {
                userState.SelectedProductId = selectedProduct.Id;
                userState.State = "EnteringQuantity";
                await _cacheService.SetUserState(message.Chat.Id, userState);

                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"Вы выбрали: {selectedProduct.Name}\nВведите количество:",
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Пожалуйста, выберите товар из списка",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandleQuantityInput(Message message, UserState userState, CancellationToken cancellationToken)
        {
            if (int.TryParse(message.Text, out int quantity) && quantity > 0)
            {
                var product = await _dbService.GetProduct(userState.SelectedProductId.Value);
                userState.SelectedQuantity = quantity;
                userState.CurrentOrder = new Order
                {
                    UserId = message.Chat.Id,
                    ProductId = userState.SelectedProductId.Value,
                    Quantity = quantity,
                    TotalPrice = product.Price * quantity,
                    Status = "New"
                };

                userState.State = "AwaitingPhoneNumber";
                await _cacheService.SetUserState(message.Chat.Id, userState);

                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new[] { new KeyboardButton("Отправить номер телефона") { RequestContact = true } }
                })
                {
                    ResizeKeyboard = true
                };

                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: $"Сумма заказа: {userState.CurrentOrder.TotalPrice:C}\n" +
                          "Пожалуйста, введите номер телефона в формате +79XXXXXXXXX или нажмите кнопку 'Отправить номер телефона':",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Пожалуйста, введите корректное количество (целое число больше 0)",
                    cancellationToken: cancellationToken);
            }
        }

        private async Task HandlePhoneNumberInput(Message message, UserState userState, CancellationToken cancellationToken)
        {
            var phonePattern = new Regex(@"^\+79\d{9}$");
            if (!phonePattern.IsMatch(message.Text))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Пожалуйста, введите номер телефона в формате +79XXXXXXXXX",
                    cancellationToken: cancellationToken);
                return;
            }

            userState.CurrentOrder.PhoneNumber = message.Text;
            userState.State = "AwaitingAddress";
            await _cacheService.SetUserState(message.Chat.Id, userState);

            await _botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "Введите адрес доставки:",
                cancellationToken: cancellationToken);
        }

        private async Task HandleAddressInput(Message message, UserState userState, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message.Text))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Пожалуйста, введите корректный адрес доставки.",
                    cancellationToken: cancellationToken);
                return;
            }

            userState.CurrentOrder.DeliveryAddress = message.Text;
            userState.State = "AwaitingDate";
            await _cacheService.SetUserState(message.Chat.Id, userState);

            await _botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: "Введите дату и время доставки в формате ДД.ММ.ГГГГ ЧЧ:ММ (например, 15.12.2024 14:30).\n" +
                      "Доставка возможна только на будущие даты и с 9:00 до 21:00.",
                cancellationToken: cancellationToken);
        }

        private async Task HandleDateTimeInput(Message message, UserState userState, CancellationToken cancellationToken)
        {
            var dateTimePattern = new Regex(@"^\d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$");
            if (!dateTimePattern.IsMatch(message.Text))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Неверный формат даты и времени. Пожалуйста, используйте формат ДД.ММ.ГГГГ ЧЧ:ММ (например, 15.12.2024 14:30)",
                    cancellationToken: cancellationToken);
                return;
            }

            var parts = message.Text.Split(' ');
            if (parts.Length != 2)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Неверный формат даты и времени. Пожалуйста, используйте формат ДД.ММ.ГГГГ ЧЧ:ММ (например, 15.12.2024 14:30)",
                    cancellationToken: cancellationToken);
                return;
            }

            if (!DateTime.TryParseExact(parts[0], "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime deliveryDate))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Неверный формат даты. Пожалуйста, используйте формат ДД.ММ.ГГГГ (например, 15.12.2024)",
                    cancellationToken: cancellationToken);
                return;
            }

            if (!TimeSpan.TryParseExact(parts[1], "hh\\:mm", null, out TimeSpan deliveryTime))
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Неверный формат времени. Пожалуйста, используйте формат ЧЧ:ММ (например, 14:30)",
                    cancellationToken: cancellationToken);
                return;
            }

            var today = DateTime.Now.Date;
            if (deliveryDate < today)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Дата доставки не может быть раньше текущей даты. Пожалуйста, выберите другую дату.",
                    cancellationToken: cancellationToken);
                return;
            }

            var minTime = new TimeSpan(9, 0, 0); // 9:00
            var maxTime = new TimeSpan(21, 0, 0); // 21:00
            if (deliveryTime < minTime || deliveryTime > maxTime)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: message.Chat.Id,
                    text: "Доставка возможна только с 9:00 до 21:00. Пожалуйста, выберите другое время.",
                    cancellationToken: cancellationToken);
                return;
            }

            userState.CurrentOrder.DeliveryDate = deliveryDate;
            userState.CurrentOrder.DeliveryTime = deliveryTime;
            userState.State = "ConfirmingOrder";
            await _cacheService.SetUserState(message.Chat.Id, userState);

            // Показываем подтверждение заказа
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] 
                { 
                    InlineKeyboardButton.WithCallbackData("✅ Подтвердить", "confirm_repeat"),
                    InlineKeyboardButton.WithCallbackData("« Отмена", "menu")
                }
            });

            var product = await _dbService.GetProduct(userState.CurrentOrder.ProductId);
            await _botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: $"Проверьте детали заказа:\n" +
                      $"🛍 Товар: {product?.Name}\n" +
                      $"📦 Количество: {userState.CurrentOrder.Quantity}\n" +
                      $"💰 Сумма: {userState.CurrentOrder.TotalPrice:C}\n" +
                      $"📱 Телефон: {userState.CurrentOrder.PhoneNumber}\n" +
                      $"📍 Адрес: {userState.CurrentOrder.DeliveryAddress}\n" +
                      $"📅 Дата доставки: {userState.CurrentOrder.DeliveryDate:dd.MM.yyyy}\n" +
                      $"⏰ Время доставки: {deliveryTime:hh\\:mm}\n\n" +
                      $"Подтвердите заказ:",
                replyMarkup: keyboard,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        private async Task ShowPastOrders(long chatId, CancellationToken cancellationToken)
        {
            var orders = await _dbService.GetUserOrders(chatId);
            if (!orders.Any())
            {
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("« Назад в меню", "menu") }
                });

                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "У вас пока нет заказов.",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
                return;
            }

            foreach (var order in orders)
            {
                var product = await _dbService.GetProduct(order.ProductId);
                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("🔄 Повторить заказ", $"repeat_{order.Id}") }
                });

                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Заказ #{order.Id}\n" +
                          $"Товар: {product.Name}\n" +
                          $"Количество: {order.Quantity}\n" +
                          $"Сумма: {order.TotalPrice:C}\n" +
                          $"Статус: {order.Status}\n" +
                          $"Дата доставки: {order.DeliveryDate:dd.MM.yyyy} {order.DeliveryTime:hh\\:mm}",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
            }

            // Добавляем кнопку возврата в меню после списка заказов
            var menuKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("« Назад в меню", "menu") }
            });

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите действие:",
                replyMarkup: menuKeyboard,
                cancellationToken: cancellationToken);
        }

        private async Task RepeatOrder(long chatId, int orderId, CancellationToken cancellationToken)
        {
            var oldOrder = await _dbService.GetOrder(orderId);
            if (oldOrder == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Заказ не найден.",
                    cancellationToken: cancellationToken);
                return;
            }

            var product = await _dbService.GetProduct(oldOrder.ProductId);
            if (product == null)
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Товар больше не доступен.",
                    cancellationToken: cancellationToken);
                return;
            }

            var newOrder = new Order
            {
                UserId = chatId,
                ProductId = oldOrder.ProductId,
                Quantity = oldOrder.Quantity,
                TotalPrice = product.Price * oldOrder.Quantity,
                PhoneNumber = oldOrder.PhoneNumber,
                DeliveryAddress = oldOrder.DeliveryAddress,
                Status = "New"
            };

            var userState = await _cacheService.GetUserState(chatId);
            userState.CurrentOrder = newOrder;
            userState.State = "AwaitingDate"; // Переходим к вводу даты
            await _cacheService.SetUserState(chatId, userState);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Повтор заказа:\n" +
                      $"🛍 Товар: {product.Name}\n" +
                      $"📦 Количество: {newOrder.Quantity}\n" +
                      $"💰 Сумма: {newOrder.TotalPrice:C}\n" +
                      $"📱 Телефон: {newOrder.PhoneNumber}\n" +
                      $"📍 Адрес: {newOrder.DeliveryAddress}\n\n" +
                      $"Введите дату и время доставки в формате ДД.ММ.ГГГГ ЧЧ:ММ:",
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        private async Task NotifyAdmin(long adminChatId, Order order, int orderId, CancellationToken cancellationToken)
        {
            var product = await _dbService.GetProduct(order.ProductId);
            if (product == null) return;

            var message = $"🆕 Новый заказ №{orderId}!\n\n" +
                 $"🛍 Товар: {product.Name}\n" +
                 $"📦 Количество: {order.Quantity}\n" +
                 $"💰 Сумма: {order.TotalPrice:C}\n" +
                 $"📱 Телефон: {order.PhoneNumber}\n" +
                 $"📍 Адрес: {order.DeliveryAddress}\n" +
                 $"📅 Дата доставки: {order.DeliveryDate:dd.MM.yyyy}\n" +
                 $"⏰ Время доставки: {order.DeliveryTime:hh\\:mm}";

            await _botClient.SendTextMessageAsync(
                chatId: adminChatId,
                text: message,
                parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Error occurred: {exception.Message}");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
