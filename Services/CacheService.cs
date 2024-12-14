using System;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramWaterBot.Models;
using Microsoft.Extensions.Configuration;

namespace TelegramWaterBot.Services
{
    public class CacheService
    {
        private readonly IDatabase _redis;
        private readonly ConnectionMultiplexer _redisConnection;

        public CacheService(IConfiguration configuration)
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis"));
            options.AbortOnConnectFail = false; // Don't fail if Redis is not available
            options.ConnectTimeout = 5000; // 5 seconds
            options.SyncTimeout = 5000;
            
            _redisConnection = ConnectionMultiplexer.Connect(options);
            _redis = _redisConnection.GetDatabase();
        }

        public async Task SetUserState(long chatId, UserState state)
        {
            try
            {
                var key = $"user_state:{chatId}";
                var value = JsonSerializer.Serialize(state);
                await _redis.StringSetAsync(key, value, TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis SetUserState error: {ex.Message}");
                // Fallback to in-memory cache or just continue without caching
            }
        }

        public async Task<UserState> GetUserState(long chatId)
        {
            try
            {
                var key = $"user_state:{chatId}";
                var value = await _redis.StringGetAsync(key);
                
                if (value.IsNull)
                    return new UserState { ChatId = chatId, State = "Start" };

                return JsonSerializer.Deserialize<UserState>(value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis GetUserState error: {ex.Message}");
                return new UserState { ChatId = chatId, State = "Start" };
            }
        }

        public async Task ClearUserState(long chatId)
        {
            try
            {
                var key = $"user_state:{chatId}";
                await _redis.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis ClearUserState error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _redisConnection?.Dispose();
        }
    }
}
