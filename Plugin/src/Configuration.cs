using System;
using System.Collections.Generic;
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
        public ConfigEntry<int> SpawnWeight;
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
        public ConfigEntry<bool> CustomSpawnweightPerLevel;
        public ConfigEntry<float> DefaultSpeed;
        public ConfigEntry<int> MaxTrexCount;
        //public ConfigEntry<int> PlayersToEat;

        public PluginConfig(ConfigFile cfg)
        {
            InitInstance(this);

            MaxTrexCount = cfg.Bind("Spawning - General", "Max T-rex Count", 1, "Increasing this number makes it possible for more T-rexes to spawn naturally.");

            DefaultSpeed = cfg.Bind("General", "Default speed", 6f, "The default speed of the T-rex. Increasing the default speed will also increase the chasing speed e.g. default speed * 2 = chasing speed");

/*            PlayersToEat = cfg.Bind("General", "Amount of players to eat untill no longer the T-rex is no longer hungry", 2, "When the T-rex \"eats\" a player their body is gone/no longer teleportable, similar to a Forest Giant.\n" +
                "After the T-rex is no longer hungry it will drop the bodies of players in it's mouth");*/

            SpawnWeight = cfg.Bind("Spawning - General", "Spawn weight", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            CustomSpawnweightPerLevel = cfg.Bind("Spawning - General", "Enable custom spawn weights per level", false,
                "Enabled = The spawn weights set for each moon will be used for the respective moon | Disabled = The general/default spawn weight will be applied globally");

            SpawnWeightExperimentation = cfg.Bind("Spawning - Moons", "Spawn weight Experimentation", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightAssurance = cfg.Bind("Spawning - Moons", "Spawn weight Assurance", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightVow = cfg.Bind("Spawning - Moons", "Spawn weight Vow", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightMarch = cfg.Bind("Spawning - Moons", "Spawn weight March", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightOffense = cfg.Bind("Spawning - Moons", "Spawn weight Offense", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightRend = cfg.Bind("Spawning - Moons", "Spawn weight Rend", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightDine = cfg.Bind("Spawning - Moons", "Spawn weight Dine", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightTitan = cfg.Bind("Spawning - Moons", "Spawn weight Titan", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightAdamance = cfg.Bind("Spawning - Moons", "Spawn weight Adamance", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightArtifice = cfg.Bind("Spawning - Moons", "Spawn weight Artifice", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightEmbrion = cfg.Bind("Spawning - Moons", "Spawn weight Embrion", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

            SpawnWeightModded = cfg.Bind("Spawning - Moons", "Spawn weight modded Moons", 20,
                "The spawn chance weight for JPOGTrex, relative to other existing enemies.\n" +
                "Goes up from 0, lower is more rare, 100 and up is very common.");

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