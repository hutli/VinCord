using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VinCord
{
  /// <summary>
  /// Service class that provides access to Vintage Story API and mod configuration
  /// for Discord command modules via dependency injection.
  /// </summary>
  public class VinCordService
  {
    public ICoreServerAPI Api { get; }
    public VinCordConfig Config { get; }

    public VinCordService(ICoreServerAPI api, VinCordConfig config)
    {
      Api = api;
      Config = config;
    }

    public BlockPos AbsoluteToPretty(BlockPos pos)
    {
      return new BlockPos(pos.X - Api.WorldManager.DefaultSpawnPosition[0], pos.Y, pos.Z - Api.WorldManager.DefaultSpawnPosition[2]);
    }

    /// <summary>
    /// Saves the current configuration to disk.
    /// </summary>
    public void SaveConfig()
    {
      Api.StoreModConfig(Config, "VinCord.json");
    }

    #region Weather

    /// <summary>
    /// Gets the climate at the home location, or null if no home is set.
    /// </summary>
    public ClimateCondition GetHomeClimate()
    {
      var home = Config.HomeLocation;
      if (home == null) return null;

      return Api.World.BlockAccessor.GetClimateAt(home, EnumGetClimateMode.NowValues);
    }

    /// <summary>
    /// Gets an appropriate weather emoji based on climate conditions.
    /// </summary>
    public string GetWeatherEmoji(ClimateCondition climate)
    {
      if (climate == null) return "❓";

      if (climate.Temperature < 0)
      {
        return climate.Rainfall > 0.3f ? "🌨️" : "❄️";
      }
      if (climate.Rainfall > 0.6f) return "🌧️";
      if (climate.Rainfall > 0.3f) return "🌦️";
      if (climate.Rainfall > 0.1f) return "⛅";
      return "☀️";
    }

    /// <summary>
    /// Gets an appropriate moon phase emoji.
    /// </summary>
    public string GetMoonEmoji(EnumMoonPhase moonPhase)
    {
      return moonPhase switch
      {
        EnumMoonPhase.Empty => "🌑",
        EnumMoonPhase.Grow1 => "🌒",
        EnumMoonPhase.Grow2 => "🌓",
        EnumMoonPhase.Grow3 => "🌔",
        EnumMoonPhase.Full => "🌕",
        EnumMoonPhase.Shrink1 => "🌖",
        EnumMoonPhase.Shrink2 => "🌗",
        EnumMoonPhase.Shrink3 => "🌘",
        _ => "🌚"
      };
    }

    #endregion
  }
}