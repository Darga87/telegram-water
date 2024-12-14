using System;
using System.Collections.Generic;

namespace TelegramWaterBot.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; } // Добавляем поле для URL изображения
        public int StockQuantity { get; set; } // Добавляем поле для остатка
        public bool IsAvailable { get; set; } = true; // Флаг доступности товара
    }

    public class Order
    {
        public int Id { get; set; }
        public long UserId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal TotalPrice { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string DeliveryAddress { get; set; } = string.Empty;
        public DateTime? DeliveryDate { get; set; }
        public TimeSpan? DeliveryTime { get; set; }
        public string Status { get; set; } = "New";
    }

    public class UserState
    {
        public long ChatId { get; set; }
        public string? State { get; set; }
        public Order? CurrentOrder { get; set; }
        public int? SelectedProductId { get; set; }
        public int? SelectedQuantity { get; set; }
        public Dictionary<string, string> TempData { get; set; } = new Dictionary<string, string>();
    }
}
