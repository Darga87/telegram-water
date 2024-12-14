using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Npgsql;
using TelegramWaterBot.Models;
using Microsoft.Extensions.Configuration;

namespace TelegramWaterBot.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly int _maxRetries = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(5);

        public DatabaseService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        private async Task<T> ExecuteWithRetry<T>(Func<NpgsqlConnection, Task<T>> operation)
        {
            Exception lastException = null;

            for (int i = 0; i < _maxRetries; i++)
            {
                try
                {
                    using var connection = new NpgsqlConnection(_connectionString);
                    await connection.OpenAsync();
                    return await operation(connection);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"Database operation failed (attempt {i + 1}/{_maxRetries}): {ex.Message}");
                    
                    if (i < _maxRetries - 1)
                    {
                        await Task.Delay(_retryDelay);
                    }
                }
            }

            throw new Exception($"Database operation failed after {_maxRetries} attempts", lastException);
        }

        public async Task InitializeDatabase()
        {
            await ExecuteWithRetry<int>(async connection =>
            {
                await connection.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS Products (
                        Id SERIAL PRIMARY KEY,
                        Name VARCHAR(100),
                        Description TEXT,
                        Price DECIMAL,
                        ImageUrl VARCHAR(255)
                    );

                    CREATE TABLE IF NOT EXISTS Orders (
                        Id SERIAL PRIMARY KEY,
                        UserId BIGINT,
                        ProductId INT,
                        Quantity INT,
                        TotalPrice DECIMAL,
                        PhoneNumber VARCHAR(15),
                        DeliveryAddress TEXT,
                        DeliveryDate DATE,
                        DeliveryTime TIME,
                        Status VARCHAR(50),
                        CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (ProductId) REFERENCES Products(Id)
                    );

                    -- Insert test products if they don't exist
                    INSERT INTO Products (Name, Description, Price, ImageUrl)
                    SELECT 'Вода 19л', 'Питьевая вода в многоразовой таре', 299.99, 'https://example.com/water19l.jpg'
                    WHERE NOT EXISTS (SELECT 1 FROM Products WHERE Name = 'Вода 19л');

                    INSERT INTO Products (Name, Description, Price, ImageUrl)
                    SELECT 'Вода 5л', 'Питьевая вода в пластиковой бутылке', 129.99, 'https://example.com/water5l.jpg'
                    WHERE NOT EXISTS (SELECT 1 FROM Products WHERE Name = 'Вода 5л');

                    INSERT INTO Products (Name, Description, Price, ImageUrl)
                    SELECT 'Вода 0.5л', 'Питьевая вода в малой таре', 49.99, 'https://example.com/water05l.jpg'
                    WHERE NOT EXISTS (SELECT 1 FROM Products WHERE Name = 'Вода 0.5л');");
                
                return 1;
            });
        }

        public async Task<IEnumerable<Product>> GetAllProducts()
        {
            return await ExecuteWithRetry(async connection =>
                await connection.QueryAsync<Product>("SELECT * FROM Products"));
        }

        public async Task<Product> GetProduct(int id)
        {
            return await ExecuteWithRetry(async connection =>
                await connection.QueryFirstOrDefaultAsync<Product>(
                    "SELECT * FROM Products WHERE Id = @Id", new { Id = id }));
        }

        public async Task<int> CreateOrder(Order order)
        {
            using var connection = new NpgsqlConnection(_connectionString);
            var sql = @"
                INSERT INTO Orders (
                    UserId, ProductId, Quantity, TotalPrice, 
                    PhoneNumber, DeliveryAddress, DeliveryDate, 
                    DeliveryTime, Status
                ) 
                VALUES (
                    @UserId, @ProductId, @Quantity, @TotalPrice, 
                    @PhoneNumber, @DeliveryAddress, @DeliveryDate, 
                    @DeliveryTime, @Status
                ) 
                RETURNING Id";

            return await connection.QuerySingleAsync<int>(sql, order);
        }

        public async Task<IEnumerable<Order>> GetUserOrders(long userId)
        {
            return await ExecuteWithRetry(async connection =>
                await connection.QueryAsync<Order>(
                    "SELECT * FROM Orders WHERE UserId = @UserId ORDER BY CreatedAt DESC",
                    new { UserId = userId }));
        }

        public async Task UpdateOrderStatus(int orderId, string status)
        {
            await ExecuteWithRetry(async connection =>
                await connection.ExecuteAsync(
                    "UPDATE Orders SET Status = @Status WHERE Id = @Id",
                    new { Id = orderId, Status = status }));
        }

        public async Task<Order?> GetOrder(int orderId)
        {
            return await ExecuteWithRetry(async connection =>
                await connection.QueryFirstOrDefaultAsync<Order>(
                    "SELECT * FROM Orders WHERE Id = @Id", new { Id = orderId }));
        }
    }
}
