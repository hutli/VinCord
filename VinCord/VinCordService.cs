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

        /// <summary>
        /// Saves the current configuration to disk.
        /// </summary>
        public void SaveConfig()
        {
            Api.StoreModConfig(Config, "VinCord.json");
        }

        #region Coordinate Conversion

        /// <summary>
        /// Gets the world's center X coordinate (half of map size X).
        /// </summary>
        public int WorldCenterX => Api.World.BlockAccessor.MapSizeX / 2;

        /// <summary>
        /// Gets the world's center Z coordinate (half of map size Z).
        /// </summary>
        public int WorldCenterZ => Api.World.BlockAccessor.MapSizeZ / 2;

        /// <summary>
        /// Converts absolute coordinates to pretty coordinates (relative to world center).
        /// Pretty coordinates are what players see in the HUD.
        /// </summary>
        public (int X, int Y, int Z) AbsoluteToPretty(BlockPos absolutePos)
        {
            return (
                absolutePos.X - WorldCenterX,
                absolutePos.Y,
                absolutePos.Z - WorldCenterZ
            );
        }

        /// <summary>
        /// Converts absolute coordinates to pretty coordinates (relative to world center).
        /// </summary>
        public (int X, int Y, int Z) AbsoluteToPretty(int absX, int absY, int absZ)
        {
            return (
                absX - WorldCenterX,
                absY,
                absZ - WorldCenterZ
            );
        }

        /// <summary>
        /// Converts pretty coordinates (relative to world center) to absolute coordinates.
        /// Absolute coordinates are what the API uses internally.
        /// </summary>
        public BlockPos PrettyToAbsolute(int prettyX, int prettyY, int prettyZ)
        {
            return new BlockPos(
                prettyX + WorldCenterX,
                prettyY,
                prettyZ + WorldCenterZ
            );
        }

        /// <summary>
        /// Formats a BlockPos as a pretty coordinate string for display to users.
        /// </summary>
        public string FormatPrettyCoords(BlockPos absolutePos)
        {
            var (x, y, z) = AbsoluteToPretty(absolutePos);
            return $"({x}, {y}, {z})";
        }

        #endregion

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