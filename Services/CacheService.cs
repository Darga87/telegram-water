using System;
using System.Text.Json;
using System.Threading.Tasks;
using StackExchange.Redis;
using TelegramWaterBot.Models;

namespace TelegramWaterBot.Services
{
    public class CacheService
    {
        private readonly ConnectionMultiplexer _redis;

        public CacheService(string configuration)
        {
            if (string.IsNullOrEmpty(configuration))
                throw new ArgumentNullException(nameof(configuration));

            _redis = ConnectionMultiplexer.Connect(configuration);
        }

        public async Task SetUserState(long chatId, UserState state)
        {
            try
            {
                var db = _redis.GetDatabase();
                var key = $"user_state:{chatId}";
                var value = JsonSerializer.Serialize(state);
                await db.StringSetAsync(key, value, TimeSpan.FromHours(1));
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
                var db = _redis.GetDatabase();
                var value = await db.StringGetAsync($"user_state:{chatId}");

                if (!value.HasValue)
                    return new UserState { ChatId = chatId, State = "Start" };

                var state = JsonSerializer.Deserialize<UserState>(value!) ?? 
                           new UserState { ChatId = chatId, State = "Start" };
                return state;
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
                var db = _redis.GetDatabase();
                var key = $"user_state:{chatId}";
                await db.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Redis ClearUserState error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _redis?.Dispose();
        }
    }
}
