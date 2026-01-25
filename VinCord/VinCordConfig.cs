using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace VinCord
{
    public class ChannelOverride
    {
        public ulong DiscordChannel { get; set; } = 0;
        public bool ChatToDiscord { get; set; } = true;
        public bool ChatToGame { get; set; } = true;
    }
    public class VinCordConfig
    {
        public string DiscordToken { get; set; } = "";
        public ChannelOverride DefaultChannel { get; set; } = new ChannelOverride();
        public BlockPos HomeLocation { get; set; } = null;
        public string DefaultNickname { get; set; } = "";
        public double MinPresenceUpdateWait { get; set; } = 20.0;
        public string[] IgnoreDiscordUsers { get; set; } = new string[] { };
        public bool PlayerDeathToDiscord { get; set; } = true;
        public bool AllowMentions { get; set; } = false;
        public string PlayerJoinMessage { get; set; } = "**[{0}]** `joined 👋`";
        public string PlayerLeaveMessage { get; set; } = "**[{0}]** `left ✌️`";
        public string ServerStartMessage { get; set; } = "# `🎉Server started!🎉`";
        public string ServerShutdownMessage { get; set; } = "# `‼️Server shutdown‼️`";
        public string MoonFullMessage { get; set; } = "A full moon rises - do you hear the howling?";
        public string MoonWainingMessage { get; set; } = "The full moon is waining - the night is quiet again...";
        public Dictionary<int, string> MonthMessages { get; set; } =
            new Dictionary<int, string>
            {
          {1, "January is here - Happy New Year! What will the new year bring?"},
          {2, "February is here - Last month of the winter, you can do this!"},
          {3, "March is here - Spring has started, let's get planting!"},
          {4, "April is here - Please no more snow!"},
          {5, "May is here - Last month of the spring, I hope you have those crops in the ground!"},
          {6, "June is here - Summer has started, you want to go to the beach?"},
          {7, "July is here - This heat is almost making me miss winter."},
          {8, "August is here - Last month of the Summer, enjoy the weather while you can!"},
          {9, "September is here - Autumn has arrived, better start stocking up on food."},
          {10, "October is here - What to wear for halloween?"},
          {11, "November is here - Last month of Autumn, do you have enough food?"},
          {12, "December is here - First month of Winter, hope the cellar is stocked."}
            };
        public Dictionary<string, string> LogScrapeRegexes { get; set; } =
            new Dictionary<string, string>
            {
                [@"^Message to all in group 0: (A .* temporal storm is imminent)$"] = @"$1",
                [@"^Message to all in group 0: (The temporal storm seems to be waning)$"] = @"$1",
                [@"^(All clients disconnected, pausing game calendar.)$"] = @"$1",
                [@"^(A client reconnected, resuming game calendar.)$"] = @"$1"
            };
    }
}