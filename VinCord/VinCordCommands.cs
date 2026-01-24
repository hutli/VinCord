using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Vintagestory.API.Common;

namespace VinCord
{
    public class HomeCommands : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly VinCordService _vincord;

        // Constructor injection - Discord.Net's InteractionService will 
        // automatically inject the VinCordService when this module is created
        public HomeCommands(VinCordService vincord)
        {
            _vincord = vincord;
        }

        [SlashCommand("home", "Gets the home base location")]
        public async Task GetHome()
        {
            var home = _vincord.Config.HomeLocation;
            if (home == null)
            {
                await RespondAsync("No home location has been set yet.", ephemeral: true);
                return;
            }

            await RespondAsync($"🏠 Home base: **{_vincord.FormatPrettyCoords(home)}**");
        }

        [SlashCommand("sethome", "Sets the home base location (use pretty coordinates from HUD)")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetHome(
            [Summary("x", "X coordinate (pretty/HUD coordinate)")] int x,
            [Summary("y", "Y coordinate")] int y,
            [Summary("z", "Z coordinate (pretty/HUD coordinate)")] int z)
        {
            // Convert pretty coordinates to absolute for internal storage
            var absolutePos = _vincord.PrettyToAbsolute(x, y, z);
            _vincord.Config.HomeLocation = absolutePos;
            _vincord.SaveConfig();

            _vincord.Api.Server.LogNotification($"[VinCord] Home location set to pretty ({x}, {y}, {z}) / absolute ({absolutePos.X}, {absolutePos.Y}, {absolutePos.Z})");
            await RespondAsync($"✅ Home base set to: **({x}, {y}, {z})**");
        }

        [SlashCommand("setnickname", "Sets the bot's default nickname for presence updates")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task SetNickname(
            [Summary("nickname", "The base nickname to use (weather/moon info will be appended)")] string nickname)
        {
            _vincord.Config.DefaultNickname = nickname;
            _vincord.SaveConfig();

            _vincord.Api.Server.LogNotification($"[VinCord] Default nickname set to: {nickname}");
            await RespondAsync($"✅ Default nickname set to: **{nickname}**");
        }

        [SlashCommand("nickname", "Gets the bot's current default nickname")]
        public async Task GetNickname()
        {
            var nickname = _vincord.Config.DefaultNickname;
            if (string.IsNullOrEmpty(nickname))
            {
                await RespondAsync("No default nickname has been set. The bot's username will be used.", ephemeral: true);
                return;
            }

            await RespondAsync($"🏷️ Default nickname: **{nickname}**");
        }

        [SlashCommand("players", "Shows online players")]
        public async Task Players()
        {
            var players = _vincord.Api.World.AllOnlinePlayers;

            if (players.Length == 0)
            {
                await RespondAsync("No players are currently online.");
                return;
            }

            var embed = new EmbedBuilder()
                .WithTitle($"🎮 Online Players ({players.Length})")
                .WithColor(Color.Green);

            foreach (var player in players)
            {
                embed.AddField(player.PlayerName, "Online", inline: true);
            }

            await RespondAsync(embed: embed.Build());
        }

        [SlashCommand("time", "Shows the current in-game time")]
        public async Task Time()
        {
            var calendar = _vincord.Api.World.Calendar;
            int hour = (int)calendar.HourOfDay;
            int minute = (int)(60.0 * (calendar.HourOfDay % 1));

            await RespondAsync($"🕐 In-game time: **{hour:D2}:{minute:D2}** (Day {calendar.DayOfYear + 1}, Year {calendar.Year})");
        }

        [SlashCommand("weather", "Shows the weather at the home location")]
        public async Task Weather()
        {
            var home = _vincord.Config.HomeLocation;
            if (home == null)
            {
                await RespondAsync("No home location has been set. Use `/sethome` first.", ephemeral: true);
                return;
            }

            var climate = _vincord.Api.World.BlockAccessor.GetClimateAt(home, EnumGetClimateMode.NowValues);
            if (climate == null)
            {
                await RespondAsync("Could not retrieve climate data for the home location.", ephemeral: true);
                return;
            }

            // Temperature is in Celsius
            float tempC = climate.Temperature;

            // Rainfall is 0-1, convert to percentage
            float rainPercent = climate.Rainfall * 100f;

            // Get weather description based on conditions
            string weatherEmoji = _vincord.GetWeatherEmoji(climate);
            string weatherDesc = GetWeatherDescription(climate);

            var embed = new EmbedBuilder()
                .WithTitle($"{weatherEmoji} Weather at Home Base")
                .WithDescription(weatherDesc)
                .WithColor(GetWeatherColor(climate))
                .AddField("🌡️ Temperature", $"{tempC:F1}°C", inline: true)
                .AddField("💧 Rainfall", $"{rainPercent:F0}%", inline: true)
                .AddField("📍 Location", _vincord.FormatPrettyCoords(home), inline: true)
                .WithFooter($"Humidity: {climate.WorldgenRainfall * 100:F0}% • Fertility: {climate.Fertility * 100:F0}%");

            await RespondAsync(embed: embed.Build());
        }

        private string GetWeatherDescription(ClimateCondition climate)
        {
            if (climate.Temperature < -10) return "Freezing cold!";
            if (climate.Temperature < 0)
            {
                return climate.Rainfall > 0.3f ? "Snowy conditions" : "Cold and clear";
            }
            if (climate.Temperature < 10)
            {
                if (climate.Rainfall > 0.5f) return "Cold and rainy";
                return "Cool weather";
            }
            if (climate.Temperature < 20)
            {
                if (climate.Rainfall > 0.5f) return "Mild with rain";
                return "Pleasant weather";
            }
            if (climate.Temperature < 30)
            {
                if (climate.Rainfall > 0.5f) return "Warm and rainy";
                return "Warm and sunny";
            }
            return climate.Rainfall > 0.3f ? "Hot and humid" : "Hot and dry";
        }

        private Color GetWeatherColor(ClimateCondition climate)
        {
            if (climate.Temperature < 0) return new Color(135, 206, 235); // Light blue for cold
            if (climate.Rainfall > 0.5f) return new Color(70, 130, 180);  // Steel blue for rain
            if (climate.Temperature > 25) return new Color(255, 165, 0);  // Orange for hot
            return new Color(50, 205, 50);                                 // Lime green for pleasant
        }
    }
}