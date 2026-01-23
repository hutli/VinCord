using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Collections;

namespace VinCord
{
  /*
   * This is the entry point for the mod. ModSystems will be automatically detected and contain a load of useful functions for loading mods.
   * Take a look at https://apidocs.vintagestory.at/api/Vintagestory.API.Common.ModSystem.html for more info.
   */
  public class VinCordModSystem : ModSystem
  {
    /// <summary>
    /// This function is automatically called only on the server when a world is loaded.
    /// It is often used to load server-side configs, or create server-side commands.
    /// </summary>
    /// <param name="api"></param>

    ICoreServerAPI api;
    VinCordConfig config;
    DiscordSocketClient client;
    int currentMonth = 0;
    public SocketTextChannel channel = null;

    public override void StartServerSide(ICoreServerAPI api)
    {
      this.api = api;

      config = api.LoadModConfig<VinCordConfig>("VinCord.json");
      if (config == null)
      {
        config = new VinCordConfig();
        api.StoreModConfig<VinCordConfig>(config, "VinCord.json");
      }

      if (config.DiscordToken == "")
      {
        api.Server.LogError("[VinCord] Discord token not provided - exiting!");
        Dispose();
        return;
      }
      if (config.DefaultChannel.DiscordChannel == 0)
      {
        api.Server.LogError("[VinCord] default Discord channel not provided - exiting!");
        Dispose();
        return;
      }
      StartDiscord().Wait();
    }

    async Task StartDiscord()
    {
      client = new DiscordSocketClient(new DiscordSocketConfig()
      {
        GatewayIntents = Discord.GatewayIntents.MessageContent | Discord.GatewayIntents.GuildPresences | Discord.GatewayIntents.AllUnprivileged
      });

      api.Server.LogNotification("[VinCord] attempting to login.");
      await client.LoginAsync(TokenType.Bot, config.DiscordToken);

      await client.StartAsync();

      api.Server.LogNotification("[VinCord] client started.");

      TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
      client.Disconnected += (Exception e) =>
      {
        api.Server.LogError("[VinCord] disconnected from Discord: {0}.", e);
        ready.TrySetResult(false);
        return Task.CompletedTask;
      };
      client.Ready += () =>
      {
        ready.SetResult(true);
        return Task.CompletedTask;
      };
      if (!await ready.Task)
      {
        client = null;
        return;
      }

      api.Server.LogNotification("[VinCord] connection ready.");

      channel = client.GetChannel(config.DefaultChannel.DiscordChannel) as SocketTextChannel;
      if (channel == null)
      {
        api.Server.LogWarning("[VinCord] Cannot resolve channel {0}.", config.DefaultChannel.DiscordChannel);
      }

      client.MessageReceived += DiscordMessageReceived;
      api.Event.PlayerChat += OnPlayerChat;
      api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
      api.Event.PlayerDisconnect += OnPlayerDisconnect;
      api.Event.PlayerDeath += OnPlayerDeath;
      api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnRunGame);
      api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnShutdown);
      api.Server.Logger.EntryAdded += OnLogMessage;
      api.Server.LogNotification("[VinCord] loaded");
    }
    public override void Dispose()
    {
      api.Server.LogNotification("[VinCord] shutting down");

      api.Event.PlayerChat -= OnPlayerChat;
      api.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
      api.Event.PlayerDisconnect -= OnPlayerDisconnect;
      api.Event.PlayerDeath -= OnPlayerDeath;
      api.Server.Logger.EntryAdded -= OnLogMessage;

      client?.LogoutAsync().Wait();
      client?.Dispose();

      api.Server.LogNotification("[VinCord] logged out of discord.");

      base.Dispose();
    }

    private void OnLogMessage(EnumLogType logType, string message, object[] args)
    {
      if (channel == null) return;

      if (config.LogScrapeRegexes.Count == 0) return;

      string formatted = string.Format(message, args);
      foreach (KeyValuePair<string, string> item in config.LogScrapeRegexes)
      {
        Match m = Regex.Match(formatted, item.Key);
        if (!m.Success)
        {
          continue;
        }

        string result = m.Result(item.Value);
        if (result.Length != 0) channel.SendMessageAsync($"## `{result}`");
        return;
      }
    }

    private void OnShutdown()
    {
      api.Server.LogNotification("[VinCord] received shutdown game phase notification.");
      if (channel == null) return;
      if (config.ServerShutdownMessage.Length == 0) return;

      channel.SendMessageAsync(config.ServerShutdownMessage).Wait();
    }

    private void OnRunGame()
    {
      currentMonth = GetInGameDateTime().Month;
      double secondsPerMinute = 60.0 / (api.World.Calendar.SpeedOfTime * api.World.Calendar.CalendarSpeedMul);
      if (secondsPerMinute < config.MinPresenceUpdateWait)
      {
        api.Server.LogWarning($"[VinCord] Seconds per game minute is faster than Discord precence update speed, defaulting to {config.MinPresenceUpdateWait}s");
        api.Event.Timer(() => UpdatePresence(), config.MinPresenceUpdateWait);
      }
      else
      {
        api.Event.Timer(() => UpdatePresence(), secondsPerMinute);
      }
      if (channel == null) return;
      if (config.ServerStartMessage.Length == 0) return;

      channel.SendMessageAsync(config.ServerStartMessage);
    }
    private Task DiscordMessageReceived(SocketMessage arg)
    {
      ulong channelId = arg.Channel.Id;
      if (channel.Id != channelId)
      {
        return Task.CompletedTask;
      }
      if (!config.DefaultChannel.ChatToGame)
      {
        return Task.CompletedTask;
      }
      if (client == null)
      {
        return Task.CompletedTask;
      }
      if (client.CurrentUser.Id == arg.Author.Id)
      {
        return Task.CompletedTask;
      }
      foreach (string user in config.IgnoreDiscordUsers)
      {
        if (arg.Author.Username == user)
        {
          return Task.CompletedTask;
        }
      }
      string msg = String.Format("[{0}]: {1}", arg.Author.Username, arg.Content);

      api.BroadcastMessageToAllGroups(msg, EnumChatType.Notification);

      return Task.CompletedTask;
    }
    private void OnPlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data, BoolRef consumed)
    {
      AllowedMentions allowedMentions;
      if (config.AllowMentions)
      {
        allowedMentions = AllowedMentions.All;
      }
      else
      {
        allowedMentions = AllowedMentions.None;
      }
      string stripped = Regex.Replace(message, @"^((<.*>)?[^<>:]+:(</[^ ]*>)?) (.*)$", "$4");
      channel.SendMessageAsync(string.Format("**[{0}]** {1}", byPlayer.PlayerName, stripped), allowedMentions: allowedMentions);
    }
    private void OnPlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
    {
      if (channel == null) return;
      if (!config.PlayerDeathToDiscord) return;

      EnumDamageSource type;
      if (damageSource == null)
      {
        type = EnumDamageSource.Suicide;
      }
      else
      {
        type = damageSource.Source;
      }
      string message;
      switch (type)
      {
        case EnumDamageSource.Block:
          message = "**[{0}]** `was killed by a block 💀`";
          break;
        case EnumDamageSource.Player:
          message = "**[{0}]** `was killed by {1} 💀`";
          break;
        case EnumDamageSource.Entity:
          message = "**[{0}]** `was killed by a {1} 💀`";
          break;
        case EnumDamageSource.Fall:
          message = "**[{0}]** `fell too far 💀`";
          break;
        case EnumDamageSource.Drown:
          message = "**[{0}]** `drowned 💀`";
          break;
        case EnumDamageSource.Explosion:
          message = "**[{0}]** `blew up 💀`";
          break;
        case EnumDamageSource.Suicide:
          message = "**[{0}]** `couldn't take it anymore 💀`";
          break;
        default:
          message = "**[{0}]** `died under mysterious circumstances 💀`";
          break;
      }
      string sourceEntity = damageSource?.SourceEntity?.GetName() ?? "null";
      channel.SendMessageAsync(string.Format(message, byPlayer.PlayerName, sourceEntity));
    }

    private void OnPlayerDisconnect(IServerPlayer byPlayer)
    {
      // The player is in the process of logging out, but isn't
      // completely gone yet. So just subract one from the list
      // of currently players when updating the bot status.
      UpdatePresence(-1);

      if (channel == null) return;
      if (config.PlayerLeaveMessage.Length == 0) return;

      channel.SendMessageAsync(string.Format(config.PlayerLeaveMessage, byPlayer.PlayerName));
    }

    private void OnPlayerNowPlaying(IServerPlayer byPlayer)
    {
      UpdatePresence();

      if (channel == null) return;
      if (config.PlayerJoinMessage.Length == 0) return;

      channel.SendMessageAsync(string.Format(config.PlayerJoinMessage, byPlayer.PlayerName));
    }

    private string FormatDay(int day)
    {
      return day switch
      {
        1 => $"{day}st",
        2 => $"{day}nd",
        3 => $"{day}rd",
        _ => $"{day}th",
      };
    }
    private string FormatMonth(int month)
    {
      return month switch
      {
        1 => "jan",
        2 => "feb",
        3 => "mar",
        4 => "apr",
        5 => "may",
        6 => "jun",
        7 => "jul",
        8 => "aug",
        9 => "sep",
        10 => "oct",
        11 => "nov",
        12 => "dec",
        _ => $"{month}"
      };
    }
    private string MoonToEmoji(EnumMoonPhase moonPhase)
    {
      return moonPhase switch
      {
        EnumMoonPhase.Empty => "🌑",
        EnumMoonPhase.Full => "🌕",
        EnumMoonPhase.Grow1 => "🌒",
        EnumMoonPhase.Grow2 => "🌓",
        EnumMoonPhase.Grow3 => "🌔",
        EnumMoonPhase.Shrink1 => "🌘",
        EnumMoonPhase.Shrink2 => "🌗",
        EnumMoonPhase.Shrink3 => "🌘",
        _ => "🌚"
      };
    }
    private DateTime GetInGameDateTime()
    {
      double doubleHour = api.World.Calendar.HourOfDay;
      int daysPerMonth = api.World.Calendar.DaysPerMonth;
      int hourOfDay = (int)Math.Floor(doubleHour);
      int minuteOfDay = (int)Math.Floor(60.0 * (doubleHour % 1));
      int dayOfYear = api.World.Calendar.DayOfYear;
      int day = dayOfYear % daysPerMonth + 1;
      int month = dayOfYear / daysPerMonth + 1;
      int year = api.World.Calendar.Year + 1; // No year 0 in .NET
      return new DateTime(year, month, day, hourOfDay, minuteOfDay, 0);
    }
    private async void UpdatePresence(int adjust = 0)
    {
      if (client == null)
      {
        return;
      }
      int count = api.World.AllOnlinePlayers.Length + adjust;
      DateTime dateTime = GetInGameDateTime();
      if (dateTime.Month != currentMonth)
      {
        string monthMessage = config.MonthMessages.GetValueOrDefault(dateTime.Month);
        if (monthMessage == string.Empty)
        {
          api.BroadcastMessageToAllGroups(monthMessage, EnumChatType.Notification);
          await channel.SendMessageAsync($"## `{monthMessage}`");
        }
        currentMonth = dateTime.Month;
      }

      string message = $"{count} online";
      message += $" | {dateTime.Hour:D2}:{dateTime.Minute:D2}, {FormatDay(dateTime.Day)} of {FormatMonth(dateTime.Month)}, year {dateTime.Year}";

      SocketGuild guild = client.GetGuild(986622601445138472);
      SocketGuildUser user = guild.GetUser(client.CurrentUser.Id);
      string nickname = $"{user.Username} {MoonToEmoji(api.World.Calendar.MoonPhase)}";
      if (user.Nickname != nickname)
      {
        await user.ModifyAsync(x =>
        {
          x.Nickname = nickname;
        });
      }
      await client.SetCustomStatusAsync(message);
    }
  }
}
