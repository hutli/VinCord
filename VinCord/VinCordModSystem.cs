using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VinCord
{
  public class VinCordModSystem : ModSystem
  {
    private ICoreServerAPI api;
    private VinCordConfig config;
    private DiscordSocketClient client;
    private InteractionService interactionService;
    private IServiceProvider services;
    private VinCordService vincordService;

    private int currentMonth = 0;
    private Nullable<EnumMoonPhase> currentMoonPhase = null;
    public SocketTextChannel channel = null;

    private int previousPlayersOnline = 0;

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

    private async Task StartDiscord()
    {
      client = new DiscordSocketClient(new DiscordSocketConfig()
      {
        GatewayIntents = GatewayIntents.MessageContent
                         | GatewayIntents.GuildPresences
                         | GatewayIntents.AllUnprivileged
      });

      interactionService = new InteractionService(client.Rest);
      vincordService = new VinCordService(api, config);

      // Build the service provider with dependency injection
      services = new ServiceCollection()
          .AddSingleton(client)
          .AddSingleton(interactionService)
          .AddSingleton(vincordService)
          .BuildServiceProvider();

      // Hook up event handlers
      client.InteractionCreated += HandleInteraction;
      interactionService.Log += LogInteractionService;

      api.Server.LogNotification("[VinCord] attempting to login...");
      await client.LoginAsync(TokenType.Bot, config.DiscordToken);

      await client.StartAsync();

      api.Server.LogNotification("[VinCord] client started.");

      client.Disconnected += (Exception e) =>
      {
        api.Server.LogError($"[VinCord] disconnected from Discord: {e}");
        return Task.CompletedTask;
      };
      client.Ready += ClientReady;
    }

    private async Task ClientReady()
    {
      api.Server.LogNotification("[VinCord] Connection ready, registering commands...");

      try
      {
        await interactionService.AddModulesAsync(Assembly.GetExecutingAssembly(), services);
        await interactionService.RegisterCommandsGloballyAsync();
        api.Server.LogNotification($"[VinCord] registered {interactionService.SlashCommands.Count} slash commands.");
      }
      catch (HttpException ex)
      {
        string json = JsonConvert.SerializeObject(ex.Errors, Formatting.Indented);
        api.Server.LogError($"[VinCord] failed to register commands: {json}");
      }

      channel = client.GetChannel(config.DefaultChannel.DiscordChannel) as SocketTextChannel;
      if (channel == null)
      {
        api.Server.LogWarning($"[VinCord] cannot resolve channel {config.DefaultChannel.DiscordChannel}.");
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

    private async Task HandleInteraction(SocketInteraction interaction)
    {
      try
      {
        // Create an execution context for the interaction
        SocketInteractionContext ctx = new(client, interaction);

        // Execute the command
        IResult result = await interactionService.ExecuteCommandAsync(ctx, services);

        if (!result.IsSuccess)
        {
          api.Server.LogWarning($"[VinCord] command failed: {result.ErrorReason}");

          // Optionally respond with the error
          if (interaction.Type == InteractionType.ApplicationCommand)
          {
            await interaction.RespondAsync($"Error: {result.ErrorReason}", ephemeral: true);
          }
        }
      }
      catch (Exception ex)
      {
        api.Server.LogError($"[VinCord] interaction handling error: {ex}");
      }
    }

    private Task LogInteractionService(LogMessage msg)
    {
      switch (msg.Severity)
      {
        case LogSeverity.Error:
        case LogSeverity.Critical:
          api.Server.LogError($"[VinCord/Interactions] {msg.Message}");
          break;
        case LogSeverity.Warning:
          api.Server.LogWarning($"[VinCord/Interactions] {msg.Message}");
          break;
        default:
          api.Server.LogNotification($"[VinCord/Interactions] {msg.Message}");
          break;
      }
      return Task.CompletedTask;
    }

    public override void Dispose()
    {
      api.Server.LogNotification("[VinCord] shutting down");

      api.Event.PlayerChat -= OnPlayerChat;
      api.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
      api.Event.PlayerDisconnect -= OnPlayerDisconnect;
      api.Event.PlayerDeath -= OnPlayerDeath;
      api.Server.Logger.EntryAdded -= OnLogMessage;

      interactionService?.Dispose();
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
        if (result.Length != 0) channel.SendMessageAsync($"`{result}`");
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
      currentMoonPhase = api.World.Calendar.MoonPhase;
      double secondsPerMinute = 60.0 / (api.World.Calendar.SpeedOfTime * api.World.Calendar.CalendarSpeedMul);
      if (secondsPerMinute < config.MinPresenceUpdateWait)
      {
        api.Server.LogWarning($"[VinCord] Seconds per game minute is faster than Discord rate limit, using {config.MinPresenceUpdateWait}s interval.");
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
      if (channel?.Id != arg.Channel.Id) return Task.CompletedTask;
      if (!config.DefaultChannel.ChatToGame) return Task.CompletedTask;
      if (client?.CurrentUser.Id == arg.Author.Id) return Task.CompletedTask;

      foreach (string user in config.IgnoreDiscordUsers)
      {
        if (arg.Author.Username == user) return Task.CompletedTask;
      }

      string msg = $"[{arg.Author.Username}]: {arg.Content}";
      api.BroadcastMessageToAllGroups(msg, EnumChatType.Notification);

      return Task.CompletedTask;
    }
    private void OnPlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data, BoolRef consumed)
    {
      AllowedMentions allowedMentions = config.AllowMentions ? AllowedMentions.All : AllowedMentions.None;
      string stripped = Regex.Replace(message, @"^((<.*>)?[^<>:]+:(</[^ ]*>)?) (.*)$", "$4");
      channel.SendMessageAsync($"**[{byPlayer.PlayerName}]** {stripped}", allowedMentions: allowedMentions);
    }
    private void OnPlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
    {
      if (channel == null) return;
      if (!config.PlayerDeathToDiscord) return;

      string message = damageSource?.Source switch
      {
        EnumDamageSource.Block => "**[{0}]** `was killed by a block 💀`",
        EnumDamageSource.Player => "**[{0}]** `was killed by {1} ⚔️💀`",
        EnumDamageSource.Entity => "**[{0}]** `was killed by a {1} 💀`",
        EnumDamageSource.Fall => "**[{0}]** `thought they had featherfall 🪶💀`",
        EnumDamageSource.Drown => "**[{0}]** `tried to swim with the fishes 🐟💀`",
        EnumDamageSource.Explosion => "**[{0}]** `blew up 💥💀`",
        EnumDamageSource.Suicide => "**[{0}]** `couldn't take it anymore 💀`",
        EnumDamageSource.Bleed => "**[{0}]** `bled to death 🩸💀`",
        EnumDamageSource.Internal => "**[{0}]** `died on the inside 👀💀`",
        EnumDamageSource.Machine => "**[{0}]** `was terminated 🤖💀`",
        EnumDamageSource.Revive => "**[{0}]** `couldn't respawn ❓💀`",
        EnumDamageSource.Void => "**[{0}]** `was out of this world 💀`",
        EnumDamageSource.Weather => "**[{0}]** `was killed by The Zookeeper 🌩️💀`",
        _ => "**[{0}]** `died under mysterious circumstances 👽💀`"
      };

      string sourceEntity = damageSource?.SourceEntity?.GetName() ?? "null";
      channel.SendMessageAsync(string.Format(message, byPlayer.PlayerName, sourceEntity));
    }

    private void OnPlayerDisconnect(IServerPlayer byPlayer)
    {
      if (channel != null && config.PlayerLeaveMessage.Length != 0)
        channel.SendMessageAsync(string.Format(config.PlayerLeaveMessage, byPlayer.PlayerName));

      UpdatePresence(-1);
    }

    private void OnPlayerNowPlaying(IServerPlayer byPlayer)
    {
      UpdatePresence();

      if (channel != null && config.PlayerJoinMessage.Length > 0)
        channel.SendMessageAsync(string.Format(config.PlayerJoinMessage, byPlayer.PlayerName));
    }

    private string FormatDay(int day) => day switch
    {
      1 => $"{day}st",
      2 => $"{day}nd",
      3 => $"{day}rd",
      _ => $"{day}th"
    };

    private string FormatMonth(int month) => month switch
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

    private async Task CheckMoon()
    {
      string moonMessage = null;
      if (currentMoonPhase.HasValue &&
          api.World.Calendar.MoonPhase == EnumMoonPhase.Full &&
          currentMoonPhase.Value != EnumMoonPhase.Full)
      {
        moonMessage = config.MoonFullMessage;
      }
      if (currentMoonPhase.HasValue &&
          api.World.Calendar.MoonPhase != EnumMoonPhase.Full &&
          currentMoonPhase.Value == EnumMoonPhase.Full)
      {
        moonMessage = config.MoonWainingMessage;
      }
      if (!string.IsNullOrEmpty(moonMessage))
      {
        api.BroadcastMessageToAllGroups(moonMessage, EnumChatType.Notification);
        await channel.SendMessageAsync($"`{moonMessage}`");
      }
      currentMoonPhase = api.World.Calendar.MoonPhase;
    }
    private async Task CheckMonth(DateTime dateTime)
    {
      if (dateTime.Month != currentMonth)
      {
        string monthMessage = config.MonthMessages.GetValueOrDefault(dateTime.Month);
        if (!string.IsNullOrEmpty(monthMessage))
        {
          api.BroadcastMessageToAllGroups(monthMessage, EnumChatType.Notification);
          await channel.SendMessageAsync($"`{monthMessage}`");
        }
        currentMonth = dateTime.Month;
      }
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
      if (client == null) return;
      ClimateCondition climate = vincordService.GetHomeClimate();

      DateTime dateTime = GetInGameDateTime();
      await CheckMonth(dateTime);
      await CheckMoon();

      int playersOnline = api.World.AllOnlinePlayers.Length + adjust;
      string message = $"{playersOnline} online | {dateTime.Hour:D2}:{dateTime.Minute:D2}, {dateTime.Day}. {FormatMonth(dateTime.Month)}, Y{dateTime.Year}";
      if (playersOnline == 0 && this.previousPlayersOnline > 0)
      {
        await channel.SendMessageAsync($"`{config.PausingCalendarMessage}`");
      }
      else if (playersOnline > 0 && this.previousPlayersOnline == 0)
      {
        await channel.SendMessageAsync($"`{config.ResumingCalendarMessage}`");
      }
      this.previousPlayersOnline = playersOnline;

      // Include weather info if home location is set
      if (climate != null)
      {
        message += $" | {(int)Math.Round(climate.Temperature)}℃";
      }
      SocketGuild guild = client.GetGuild(986622601445138472);
      SocketGuildUser user = guild?.GetUser(client.CurrentUser.Id);

      if (user != null)
      {
        // Use configured nickname or fall back to bot's username
        string baseName = !string.IsNullOrEmpty(config.DefaultNickname)
            ? config.DefaultNickname
            : user.Username;

        string moonEmoji = vincordService.GetMoonEmoji(api.World.Calendar.MoonPhase);
        string nickname;

        // Include weather info if home location is set
        if (climate != null)
        {
          string weatherEmoji = vincordService.GetWeatherEmoji(climate);
          nickname = $"{baseName} ({weatherEmoji}|{moonEmoji})";
        }
        else
        {
          nickname = $"{baseName} ({moonEmoji})";
        }

        if (user.Nickname != nickname)
        {
          await user.ModifyAsync(x => x.Nickname = nickname);
        }
      }
      await client.SetCustomStatusAsync(message);
    }
  }
}
