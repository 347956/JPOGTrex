using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;

namespace JPOGTrex.Configuration {
    [Serializable]
    public class PluginConfig : SyncedInstance<PluginConfig>
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
/*        public ConfigEntry<int> SpawnWeight;
        public ConfigEntry<int> SpawnWeightExperimentation;
        public ConfigEntry<int> SpawnWeightAssurance;
        public ConfigEntry<int> SpawnWeightVow;
        public ConfigEntry<int> SpawnWeightMarch;
        public ConfigEntry<int> SpawnWeightOffense;
        public ConfigEntry<int> SpawnWeightRend;
        public ConfigEntry<int> SpawnWeightDine;
        public ConfigEntry<int> SpawnWeightTitan;
        public ConfigEntry<int> SpawnWeightAdamance;
        public ConfigEntry<int> SpawnWeightArtifice;
        public ConfigEntry<int> SpawnWeightEmbrion;
        public ConfigEntry<int> SpawnWeightModded;
        public ConfigEntry<bool> CustomSpawnweightPerLevel;*/
        public ConfigEntry<float> DefaultSpeed;
        public ConfigEntry<int> MaxTrexCount;
        public ConfigEntry<int> VisionRangeLength;
        public ConfigEntry<int> VisionRangeWidth;
        public ConfigEntry<float> SuspiciontDecreaseTime;
        public ConfigEntry<int> MaxSuspicionLevel;

        public ConfigEntry <bool> EnableJPOGTrex;
        public ConfigEntry<int> SuspicionIncrement;
        public ConfigEntry<int> SuspicionDecrement;

        public ConfigEntry<string> TrexRarity;

        //public ConfigEntry<int> PlayersToEat;


        private const string CATEGORY_GENERAL = "1. General";
        private const string CATEGORY_BEHAVIOR = "2. Behavior";
        private const string CATEGORY_SPAWNING_MOONS = "3. Spawning - Moons";

        [Conditional("DEBUG")]
        void LogIfDebugBuild(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        public PluginConfig(ConfigFile cfg)
        {


            InitInstance(this);


            EnableJPOGTrex = cfg.Bind(CATEGORY_GENERAL, "Enable T-Rex", true, "Use this option to Enable/Disable the T-Rex being able to spawn in game.");

            TrexRarity = cfg.Bind(CATEGORY_SPAWNING_MOONS,
                                                "T-Rex | Spawn Weight.",
                                                "Modded:20,ExperimentationLevel:20,AssuranceLevel:20,VowLevel:30,OffenseLevel:20,MarchLevel:30,RendLevel:10,DineLevel:10,TitanLevel:10,Adamance:20,Embrion:10,Artifice:40,Auralis:20",
                                                "Spawn Weight of the T-Rex on all moons, Feel free to add any moon to the list, just follow the format (also needs LLL installed for LE moons to work with this config).\n" +
                                                "Format Example: \"MyCustomLevel:20,OrionLevel:100,Etc:20,EtcTwo:20,EtcThree:20\"");

            MaxTrexCount = cfg.Bind(CATEGORY_BEHAVIOR, "Max T-Rex Count", 1, "Increasing this number makes it possible for more T-Rexes to spawn naturally.");

            DefaultSpeed = cfg.Bind(CATEGORY_BEHAVIOR, "Default speed", 6f, "The default speed of the T-Rex. Increasing the default speed will also increase the chasing speed e.g. default speed * 2 = chasing speed");

            /*            PlayersToEat = cfg.Bind("General", "Amount of players to eat until no longer the T-Rex is no longer hungry", 2, "When the T-Rex \"eats\" a player their body is gone/no longer teleportable, similar to a Forest Giant.\n" +
                            "After the T-Rex is no longer hungry it will drop the bodies of players in it's mouth");*/

            VisionRangeLength = cfg.Bind(CATEGORY_BEHAVIOR, "Vision Range length", 70, "Increasing this number will make the T-Rex detect players further away.");

            VisionRangeWidth = cfg.Bind(CATEGORY_BEHAVIOR, "Vision Range Width", 60, "Increasing this number will broaden the width of the T-Rex's vision. \n" +
                "WARNING: increasing this value too much can result in the T-rex being able to look behind itself.");

            MaxSuspicionLevel = cfg.Bind(CATEGORY_BEHAVIOR, "Max Suspicion Level", 100, "The max level/amount of suspicion the T-Rex needs before initiating a chase");

            SuspicionIncrement = cfg.Bind(CATEGORY_BEHAVIOR, "Suspicion Increment Amount", 10, "The amount by which the suspicion is increased when the T-Rex sees a player moving");

            SuspicionDecrement = cfg.Bind(CATEGORY_BEHAVIOR, "Suspicion Decrement Amount", 5, "The amount by which the suspicion is decreased when the T-Rex doesn't see players moving.");

            SuspiciontDecreaseTime = cfg.Bind(CATEGORY_BEHAVIOR, "Suspicion Decrease Time", 4f, "Time in seconds. The interval between decreasing suspicion.\n" + 
                "e.g. 4 means the suspicion will go down by the set amount every 4 seconds since the T-Rex last spotted a player moving.");


/*            SpawnWeight = cfg.Bind(CATEGORY_SPAWNING_GENERAL, "Spawn weight", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            CustomSpawnweightPerLevel = cfg.Bind(CATEGORY_SPAWNING_GENERAL, "Enable custom spawn weights per level", false,
                "Enabled = The spawn weights set for each moon will be used for the respective moon | Disabled = The general/default spawn weight will be applied globally");

            SpawnWeightExperimentation = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Experimentation", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightAssurance = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Assurance", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightVow = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Vow", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightMarch = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight March", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightOffense = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Offense", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightRend = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Rend", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightDine = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Dine", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightTitan = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Titan", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightAdamance = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Adamance", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightArtifice = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Artifice", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightEmbrion = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight Embrion", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightModded = cfg.Bind(CATEGORY_SPAWNING_MOONS, "Spawn weight modded Moons", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");*/

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg) {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }

        public static void RequestSync()
        {
            if (!IsClient) return;

            using FastBufferWriter stream = new(IntSize, Allocator.Temp);
            MessageManager.SendNamedMessage("ModName_OnRequestConfigSync", 0uL, stream);
        }

        public static void OnRequestSync(ulong clientId, FastBufferReader _)
        {
            if (!IsHost) return;

            Plugin.Logger.LogInfo($"Config sync request received from client: {clientId}");

            byte[] array = SerializeToBytes(Instance);
            int value = array.Length;

            using FastBufferWriter stream = new(value + IntSize, Allocator.Temp);

            try
            {
                stream.WriteValueSafe(in value, default);
                stream.WriteBytesSafe(array);

                MessageManager.SendNamedMessage("ModName_OnReceiveConfigSync", clientId, stream);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogInfo($"Error occurred syncing config with client: {clientId}\n{e}");
            }
        }

        public static void OnReceiveSync(ulong _, FastBufferReader reader)
        {
            if (!reader.TryBeginRead(IntSize))
            {
                Plugin.Logger.LogError("Config sync error: Could not begin reading buffer.");
                return;
            }

            reader.ReadValueSafe(out int val, default);
            if (!reader.TryBeginRead(val))
            {
                Plugin.Logger.LogError("Config sync error: Host could not sync.");
                return;
            }

            byte[] data = new byte[val];
            reader.ReadBytesSafe(ref data, val);

            SyncInstance(data);

            Plugin.Logger.LogInfo("Successfully synced config with host.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
        public static void InitializeLocalPlayer()
        {
            if (IsHost)
            {
                MessageManager.RegisterNamedMessageHandler("JPOGTrex_OnRequestConfigSync", OnRequestSync);
                Synced = true;

                return;
            }

            Synced = false;
            MessageManager.RegisterNamedMessageHandler("JPOGTrex_OnReceiveConfigSync", OnReceiveSync);
            RequestSync();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
        public static void PlayerLeave()
        {
            RevertSync();
        }
        
    }
}