using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using VinCord.Commands;
using Discord;
using Discord.WebSocket;

namespace VinCord
{
  /*
   * This is the entry point for the mod. ModSystems will be automatically detected and contain a load of useful functions for loading mods.
   * Take a look at https://apidocs.vintagestory.at/api/Vintagestory.API.Common.ModSystem.html for more info.
   */

  internal class ChannelInfo
  {
    public ChannelInfo(int gameChannel, ChannelOverride config)
    {
      this.gameChannel = gameChannel;
      this.config = config;
    }
    public SocketTextChannel channel = null;

    public int gameChannel;

    public ChannelOverride config;
  }

  public class VinCordModSystem : ModSystem
  {
    /// <summary>
    /// This function is automatically called only on the server when a world is loaded.
    /// It is often used to load server-side configs, or create server-side commands.
    /// </summary>
    /// <param name="api"></param>

    ICoreServerAPI api;
    VinCordConfig config;
    Dictionary<int, ChannelInfo> gameChannelInfo = new Dictionary<int, ChannelInfo>();
    Dictionary<ulong, ChannelInfo> discordChannelInfo = new Dictionary<ulong, ChannelInfo>();
    DiscordSocketClient client;

    public override void StartServerSide(ICoreServerAPI api)
    {
      this.api = api;

      config = api.LoadModConfig<VinCordConfig>("VinCord.json");
      if (config == null)
      {
        config = new VinCordConfig();
        api.StoreModConfig<VinCordConfig>(config, "VinCord.json");
      }
      //This will register your server commands.
      VinCordCommands.RegisterServerCommands(api);

      StartDiscord().Wait();
    }

    /// <summary>
    /// This function is automatically called only on the client when a world is loaded.
    /// It is often used to create rendering mechanics, or create client-side commands.
    /// </summary>
    /// <param name="api"></param>
    public override void StartClientSide(ICoreClientAPI api)
    {
      //Although nothing is added to it, this will register your defined client commands.
      VinCordCommands.RegisterClientCommands(api);
    }
    async Task StartDiscord()
    {
      client = new DiscordSocketClient();

      api.Server.LogNotification("[discordbot] attempting to login.");
      await client.LoginAsync(TokenType.Bot, config.DiscordToken);

      await client.StartAsync();

      api.Server.LogNotification("[discordbot] client started.");

      TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
      client.Disconnected += (Exception e) =>
      {
        api.Server.LogError("[discordbot] disconnected from Discord: {0}.", e);
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

      api.Server.LogNotification("[discordbot] connection ready.");

      foreach (KeyValuePair<ulong, ChannelInfo> item in discordChannelInfo)
      {
        item.Value.channel = client.GetChannel(item.Key) as SocketTextChannel;
        if (item.Value.channel == null)
        {
          api.Server.LogWarning("[discordbot] Cannot resolve channel {0}.", item.Key);
          continue;
        }
      }

      client.MessageReceived += DiscordMessageReceived;
    }

    private Task DiscordMessageReceived(SocketMessage arg)
    {
      ulong channelId = arg.Channel.Id;
      if (!discordChannelInfo.ContainsKey(channelId))
      {
        return Task.CompletedTask;
      }
      ChannelInfo info = discordChannelInfo[channelId];
      if (!info.config.ChatToGame)
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
      api.SendMessageToGroup(info.gameChannel, String.Format("[{0}]: {1}", arg.Author.Username, arg.Content), EnumChatType.Notification);
      return Task.CompletedTask;
    }
  }
}
