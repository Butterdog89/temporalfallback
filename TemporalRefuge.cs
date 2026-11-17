using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;

namespace TemporalRefuge;

public sealed class RefugeConfig
{
    public int Radius { get; set; } = 2000;
    public int SearchAttempts { get; set; } = 40;
}

public sealed class TemporalRefugeMod : ModSystem
{
    internal static TemporalRefugeMod? Active { get; private set; }

    private const string AnchorKey = "temporalrefuge:anchor-v1";
    private const string ConfigFile = "TemporalRefuge.json";
    private const int SearchTimeoutMilliseconds = 15000;
    private const int MaxConcurrentSearches = 2;
    private readonly Harmony harmony = new("temporalrefuge");
    private readonly Dictionary<string, RespawnRequest> requests = new();
    private IReadOnlyDictionary<string, BlockPos> beds = null!;
    private ICoreServerAPI api = null!;
    private RefugeConfig config = new();
    private ServerMain server = null!;
    private Action<ServerSystemEntitySimulation, IServerPlayer> resumeRespawn = null!;
    private RespawnRequest? resuming;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    // Simple Bed Spawn initializes its config and event handlers first.
    public override double ExecuteOrder() => 0.3;

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        server = (ServerMain)api.World;
        try
        {
            config = api.LoadModConfig<RefugeConfig>(ConfigFile) ?? new RefugeConfig();
            config.Radius = Math.Clamp(config.Radius, 1, 2000);
            config.SearchAttempts = Math.Clamp(config.SearchAttempts, 1, 100);
            api.StoreModConfig(config, ConfigFile);

            beds = BedSpawnRegistration.Bind(api.ModLoader);
            var respawn = RespawnHooks.BindRespawn();
            RespawnHooks.Install(harmony, respawn);
            resumeRespawn = respawn.CreateDelegate<Action<ServerSystemEntitySimulation, IServerPlayer>>();
        }
        catch (Exception error)
        {
            harmony.UnpatchAll(harmony.Id);
            api.Logger.Error("[TemporalRefuge] Refuge disabled. Existing respawn rules are active. {0}", error);
            return;
        }

        Active = this;
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        api.Logger.Notification("[TemporalRefuge] Enabled with Simple Bed Spawn. Refuge radius: {0} blocks.", config.Radius);
    }

    internal bool SetGearAnchor(ServerPlayer player, PlayerSpawnPos pos)
    {
        // Keep the old data format so anchors created with the handover build still load.
        if (player.Entity.Pos.Dimension != 0)
        {
            Notify(player, "Use a temporal gear in the overworld to set your refuge.");
            return !HasBed(player);
        }

        player.SetModData(AnchorKey, new[] { pos.x, pos.y ?? (int)player.Entity.Pos.Y, pos.z });
        Notify(player, $"Temporal refuge saved. Without a valid bed, you can respawn within {config.Radius} blocks of this point.");
        return !HasBed(player);
    }

    private bool HasBed(IServerPlayer player) =>
        beds.ContainsKey(player.PlayerUID) &&
        player.WorldData is ServerWorldPlayerData { SpawnPosition: { } };

    private static bool HasForcedSpawn(IServerPlayer player) =>
        player is ServerPlayer { Role: PlayerRole { ForcedSpawn: { } } };

    internal bool BeforeRespawn(ServerSystemEntitySimulation simulation, IServerPlayer player)
    {
        if (ReferenceEquals(resuming?.Player, player) || player.Entity is not { Alive: false })
        {
            return true;
        }

        if (requests.ContainsKey(player.PlayerUID) || player.Entity.Teleporting)
        {
            return false;
        }

        // Simple Bed Spawn owns bed registration, removal, and the vanilla spawn position.
        if (HasForcedSpawn(player) || HasBed(player) ||
            player.GetModData<int[]>(AnchorKey) is not { Length: 3 } anchor)
        {
            return true;
        }

        if (requests.Values.Count(request => !request.Committed) >= MaxConcurrentSearches)
        {
            Notify(player, "The server is already finding other refuges. Press respawn again in a moment.");
            return false;
        }

        var request = new RespawnRequest(simulation, player, anchor);
        requests.Add(player.PlayerUID, request);
        Notify(player, "Finding safe ground near your temporal refuge…");
        request.TimeoutId = api.Event.RegisterCallback(_ => OnTimeout(request), SearchTimeoutMilliseconds);
        FindNext(request);
        return false;
    }

    private void FindNext(RespawnRequest request)
    {
        if (!IsWaiting(request))
        {
            Finish(request);
            return;
        }

        if (HasBed(request.Player) || HasForcedSpawn(request.Player))
        {
            Resume(request, null);
            return;
        }

        // One column at a time per player. Late callbacks cannot resume a cancelled request.
        try
        {
            while (request.Attempts++ < config.SearchAttempts)
            {
                var random = api.World.Rand;
                double angle = random.NextDouble() * Math.Tau;
                double distance = Math.Sqrt(random.NextDouble()) * config.Radius;
                int x = request.Anchor[0] + (int)Math.Round(Math.Cos(angle) * distance);
                int z = request.Anchor[2] + (int)Math.Round(Math.Sin(angle) * distance);
                if (!IsCandidate(request, x, z))
                {
                    continue;
                }

                var probe = new BlockPos(x, 1, z);
                if (api.World.BlockAccessor.GetChunkAtBlockPos(probe) != null)
                {
                    if (TryLand(request, x, z)) return;
                    continue;
                }

                int chunkSize = GlobalConstants.ChunkSize;
                api.WorldManager.LoadChunkColumnPriority(x / chunkSize, z / chunkSize, new ChunkLoadOptions
                {
                    OnLoaded = () =>
                    {
                        if (!IsWaiting(request)) return;
                        if (!TryLand(request, x, z)) FindNext(request);
                    }
                });
                return;
            }

            Retry(request, "No safe landing found near your refuge. Press respawn to try again.");
        }
        catch (Exception error)
        {
            SearchFailed(request, error);
        }
    }

    private bool IsCandidate(RespawnRequest request, int x, int z)
    {
        double dx = (double)x - request.Anchor[0];
        double dz = (double)z - request.Anchor[2];
        return dx * dx + dz * dz <= (double)config.Radius * config.Radius &&
            api.World.BlockAccessor.IsValidPos(new BlockPos(x, 1, z));
    }

    private bool TryLand(RespawnRequest request, int x, int z)
    {
        try
        {
            if (HasBed(request.Player) || HasForcedSpawn(request.Player))
            {
                Resume(request, null);
                return true;
            }

            var landing = FindSafeSurface(request.Player, x, z);
            if (landing == null) return false;
            Resume(request, landing);
            return true;
        }
        catch (Exception error)
        {
            SearchFailed(request, error);
            return true;
        }
    }

    private FuzzyEntityPos? FindSafeSurface(IServerPlayer player, int x, int z)
    {
        var blocks = api.World.BlockAccessor;
        int y = blocks.GetRainMapHeightAt(x, z);
        if (y < 1 || y + 3 >= blocks.MapSizeY) return null;

        var floorPos = new BlockPos(x, y, z);
        var floor = blocks.GetBlock(floorPos);
        if (!floor.SideSolid[BlockFacing.UP.Index] || IsHazard(floor)) return null;

        for (int offset = 0; offset <= 3; offset++)
        {
            var pos = new BlockPos(x, y + offset, z);
            var solid = blocks.GetBlock(pos);
            var fluid = blocks.GetBlock(pos, BlockLayersAccess.Fluid);
            if (fluid.IsLiquid() || IsHazard(solid) ||
                (offset > 0 && solid.GetCollisionBoxes(blocks, pos) is { Length: > 0 }) ||
                (offset > 0 && server.WorldMap.GetBlockingLandClaimant(player, pos, EnumBlockAccessFlags.Use) != null))
            {
                return null;
            }
        }

        // Check support and standing clearance at the same point we pass to the engine.
        return new FuzzyEntityPos(x + 0.5, y + 1, z + 0.5, 0, 0, 0) { UsesLeft = -1 };
    }

    private static bool IsHazard(Block block)
    {
        string path = block.Code?.Path ?? "";
        return block.IsLiquid() || path.Contains("lava") || path.Contains("fire") ||
            path.Contains("spike") || path.Contains("cactus") || path.Contains("magma") || path.Contains("hot");
    }

    private void Resume(RespawnRequest request, FuzzyEntityPos? landing)
    {
        api.Event.UnregisterCallback(request.TimeoutId);
        request.TimeoutId = api.Event.RegisterCallback(_ => OnTimeout(request), SearchTimeoutMilliseconds);
        request.Committed = true;
        request.Landing = landing;
        var previous = resuming;
        resuming = request;
        try
        {
            // Use the real engine respawn to handle teleport, revival and client synchronization.
            resumeRespawn(request.Simulation, request.Player);
        }
        finally
        {
            resuming = previous;
        }
    }

    internal FuzzyEntityPos? GetRefugeOverride(ServerPlayer player, bool consume) =>
        consume && ReferenceEquals(resuming?.Player, player) ? resuming?.Landing : null;

    private bool IsWaiting(RespawnRequest request) =>
        IsCurrent(request) && !request.Committed &&
        ReferenceEquals(api.World.PlayerByUid(request.Player.PlayerUID), request.Player) &&
        request.Player.Entity is { Alive: false };

    private bool IsCurrent(RespawnRequest request) =>
        requests.TryGetValue(request.Player.PlayerUID, out var current) && ReferenceEquals(current, request);

    private void OnTimeout(RespawnRequest request)
    {
        if (!IsCurrent(request)) return;
        request.TimeoutId = 0;
        Retry(request, "Your respawn is taking longer than expected. Press respawn again once terrain has loaded.");
    }

    private void SearchFailed(RespawnRequest request, Exception error)
    {
        api.Logger.Warning("[TemporalRefuge] Refuge search ended with an error. {0}", error);
        Retry(request, "The refuge could not load. Press respawn to try again.");
    }

    private void Retry(RespawnRequest request, string message)
    {
        if (!IsCurrent(request)) return;
        Finish(request);
        if (ReferenceEquals(api.World.PlayerByUid(request.Player.PlayerUID), request.Player) &&
            request.Player.Entity is { Alive: false })
        {
            Notify(request.Player, message);
        }
    }

    private void OnPlayerDisconnect(IServerPlayer player) => EndRequestFor(player);
    internal void AfterRevive(EntityPlayer entity)
    {
        if (entity.Alive && entity.PlayerUID is { } uid &&
            requests.TryGetValue(uid, out var request) && ReferenceEquals(request.Player.Entity, entity))
        {
            Finish(request);
        }
    }

    private void EndRequestFor(IServerPlayer player)
    {
        if (requests.TryGetValue(player.PlayerUID, out var request) && ReferenceEquals(request.Player, player))
        {
            Finish(request);
        }
    }

    private void Finish(RespawnRequest request)
    {
        if (!IsCurrent(request)) return;
        requests.Remove(request.Player.PlayerUID);
        if (request.TimeoutId != 0) api.Event.UnregisterCallback(request.TimeoutId);
    }

    private static void Notify(IServerPlayer player, string message) =>
        player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);

    public override void Dispose()
    {
        if (Active != this) return;
        Active = null;
        api.Event.PlayerDisconnect -= OnPlayerDisconnect;
        foreach (var request in requests.Values.ToArray()) Finish(request);
        harmony.UnpatchAll(harmony.Id);
    }

    private sealed class RespawnRequest(ServerSystemEntitySimulation simulation, IServerPlayer player, int[] anchor)
    {
        internal ServerSystemEntitySimulation Simulation { get; } = simulation;
        internal IServerPlayer Player { get; } = player;
        internal int[] Anchor { get; } = anchor;
        internal int Attempts { get; set; }
        internal long TimeoutId { get; set; }
        internal bool Committed { get; set; }
        internal FuzzyEntityPos? Landing { get; set; }
    }
}
