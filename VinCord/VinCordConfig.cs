using System.Collections.Generic;

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
    public string[] IgnoreDiscordUsers { get; set; } = new string[] { };
    public string PlayerJoinMessage { get; set; } = "{0} joined.";
    public string PlayerLeaveMessage { get; set; } = "{0} left.";
    public string ServerStartMessage { get; set; } = "Server started.";
    public string ServerShutdownMessage { get; set; } = "Server shutdown.";
    public bool PlayerDeathToDiscord { get; set; } = true;
    public bool AllowMentions { get; set; } = false;
    public Dictionary<string, ChannelOverride> ChannelOverrides { get; set; } =
        new Dictionary<string, ChannelOverride> { ["gamegroupname"] = new ChannelOverride() };
    public Dictionary<string, string> LogScrapeRegexes { get; set; } =
        new Dictionary<string, string>
        {
          [@"^Message to all in group 0: (A .* temporal storm is imminent)$"] = @"$1",
          [@"^Message to all in group 0: (The temporal storm seems to be waning)$"] = @"$1",
        };
  }
}