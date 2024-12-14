using System;

namespace TelegramWaterBot.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? ImageUrl { get; set; } // Добавляем поле для URL изображения
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
        public string State { get; set; } = "Start";
        public int? SelectedProductId { get; set; }
        public int? SelectedQuantity { get; set; }
        public Order? CurrentOrder { get; set; }
    }
}
