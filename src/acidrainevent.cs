using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AcidRainMod
{
    public class AcidRainConfig
    {
        public float DamagePerTick = 0.25f;
        public float MinRainIntensity = 0.05f;
        public double EventChance = 0.1;
        
        public int TickIntervalMs = 2000;

        public string WarningColor = "#841414"; 
        public string SafeColor = "#148421";    
    }

    public class PlayerWeatherState
    {
        public bool IsInAcidShower = false;
        public bool IsWarned = false;
        public bool WasRainingLastTick = false;
    }

    public class AcidRainSystem : ModSystem
    {
        private ICoreServerAPI sapi;
        private AcidRainConfig config;
        private WeatherSystemBase weatherSystem;
        private Dictionary<string, PlayerWeatherState> playerStates = new Dictionary<string, PlayerWeatherState>();

        public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

        public override void StartPre(ICoreAPI api)
        {
            try {
                config = api.LoadModConfig<AcidRainConfig>("AcidRainConfig.json") ?? new AcidRainConfig();
                api.StoreModConfig(config, "AcidRainConfig.json");
            } catch { config = new AcidRainConfig(); }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.sapi = api;

            api.Event.RegisterGameTickListener(OnServerUpdate, config.TickIntervalMs);

            api.Event.PlayerLeave += (player) => {
                playerStates.Remove(player.PlayerUID);
            };

            api.Event.OnEntityDeath += OnEntityDeath;
        }

        private void OnServerUpdate(float dt)
        {
            if (weatherSystem == null) weatherSystem = sapi.ModLoader.GetModSystem<WeatherSystemBase>();
            if (weatherSystem == null) return;

            foreach (IServerPlayer player in sapi.Server.Players)
            {
                if (player.Entity == null || !player.Entity.Alive) continue;

                string uid = player.PlayerUID;
                if (!playerStates.ContainsKey(uid)) playerStates[uid] = new PlayerWeatherState();
                var state = playerStates[uid];

                double rain = weatherSystem.GetPrecipitation(player.Entity.Pos.XYZ);
                bool isRaining = rain >= config.MinRainIntensity;

                if (isRaining && !state.WasRainingLastTick)
                {
                    state.IsInAcidShower = sapi.World.Rand.NextDouble() < config.EventChance;
                }
                else if (!isRaining)
                {
                    state.IsInAcidShower = false;
                }
                state.WasRainingLastTick = isRaining;

                int rainHeight = sapi.World.BlockAccessor.GetRainMapHeightAt((int)player.Entity.Pos.X, (int)player.Entity.Pos.Z);
                bool isExposed = player.Entity.Pos.Y + 1.2 >= rainHeight;
                bool shouldBeWarnedNow = isRaining && state.IsInAcidShower && isExposed;

                if (shouldBeWarnedNow && !state.IsWarned)
                {
                    string translatedMsg = Lang.Get("acidrain:warning-msg");
                    string finalMsg = string.Format("<font color=\"{0}\">{1}</font>", config.WarningColor, translatedMsg);
                    sapi.SendMessage(player, GlobalConstants.AllChatGroups, finalMsg, EnumChatType.Notification);
                    state.IsWarned = true;
                }
                else if (!shouldBeWarnedNow && state.IsWarned)
                {
                    string translatedMsg = Lang.Get("acidrain:safe-msg");
                    string finalMsg = string.Format("<font color=\"{0}\">{1}</font>", config.SafeColor, translatedMsg);
                    sapi.SendMessage(player, GlobalConstants.AllChatGroups, finalMsg, EnumChatType.Notification);
                    state.IsWarned = false;
                }

                if (shouldBeWarnedNow && player.WorldData.CurrentGameMode == EnumGameMode.Survival)
                {
                    player.Entity.Attributes.SetLong("lastAcidDamageTick", sapi.World.ElapsedMilliseconds);
                    player.Entity.ReceiveDamage(new DamageSource() { Source = EnumDamageSource.Weather, Type = EnumDamageType.Poison }, config.DamagePerTick);
                }
            }
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            if (entity is EntityPlayer playerEntity)
            {
                long lastAcidTick = playerEntity.Attributes.GetLong("lastAcidDamageTick", 0);
                if (sapi.World.ElapsedMilliseconds - lastAcidTick < 4000)
                {
                    string name = playerEntity.GetBehavior<EntityBehaviorNameTag>()?.DisplayName ?? "Survivor";
                    string deathMsg = Lang.Get("acidrain:death-msg", name);
                    sapi.BroadcastMessageToAllGroups(deathMsg, EnumChatType.Notification);
                }
            }
        }
    }
}
