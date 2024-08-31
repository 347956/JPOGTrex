﻿using System.Reflection;
using UnityEngine;
using BepInEx;
using LethalLib.Modules;
using BepInEx.Logging;
using System.IO;
using JPOGTrex.Configuration;
using System.Collections.Generic;
using System.Linq;
using static LethalLib.Modules.Levels;

namespace JPOGTrex {
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)] 
    public class Plugin : BaseUnityPlugin {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;
        public static AssetBundle? ModAssets;

        private void Awake() {
            Logger = base.Logger;

            // If you don't want your mod to use a configuration file, you can remove this line, Configuration.cs, and other references.
            BoundConfig = new PluginConfig(base.Config);
            // This should be ran before Network Prefabs are registered.
            InitializeNetworkBehaviours();

            // We load the asset bundle that should be next to our DLL file, with the specified name.
            // You may want to rename your asset bundle from the AssetBundle Browser in order to avoid an issue with
            // asset bundle identifiers being the same between multiple bundles, allowing the loading of only one bundle from one mod.
            // In that case also remember to change the asset bundle copying code in the csproj.user file.
            var bundleName = "jpogtrexassets";
            ModAssets = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), bundleName));
            if (ModAssets == null) {
                Logger.LogError($"Failed to load custom assets.");
                return;
            }

            // We load our assets from our asset bundle. Remember to rename them both here and in our Unity project.
            var JPOGTrex = ModAssets.LoadAsset<EnemyType>("JPOGTrexObj");
            var JPOGTrexTN = ModAssets.LoadAsset<TerminalNode>("JPOGTrexTN");
            var JPOGTrexTK = ModAssets.LoadAsset<TerminalKeyword>("JPOGTrexTK");
            JPOGTrex.MaxCount = BoundConfig.MaxTrexCount.Value;

            // Optionally, we can list which levels we want to add our enemy to, while also specifying the spawn weight for each.
            /*
             * 
             * 
            var JPOGTrexLevelRarities = new Dictionary<Levels.LevelTypes, int> {
                {Levels.LevelTypes.ExperimentationLevel, 10},
                {Levels.LevelTypes.AssuranceLevel, 40},
                {Levels.LevelTypes.VowLevel, 20},
                {Levels.LevelTypes.OffenseLevel, 30},
                {Levels.LevelTypes.MarchLevel, 20},
                {Levels.LevelTypes.RendLevel, 50},
                {Levels.LevelTypes.DineLevel, 25},
                // {Levels.LevelTypes.TitanLevel, 33},
                // {Levels.LevelTypes.All, 30},     // Affects unset values, with lowest priority (gets overridden by Levels.LevelTypes.Modded)
                {Levels.LevelTypes.Modded, 60},     // Affects values for modded moons that weren't specified
            };
            // We can also specify custom level rarities
            var JPOGTrexCustomLevelRarities = new Dictionary<string, int> {
                {"EGyptLevel", 50},
                {"46 Infernis", 69},    // Either LLL or LE(C) name can be used, LethalLib will handle both
            };
            */

            // Network Prefabs need to be registered. See https://docs-multiplayer.unity3d.com/netcode/current/basics/object-spawning/
            // LethalLib registers prefabs on GameNetworkManager.Start.
            NetworkPrefabs.RegisterNetworkPrefab(JPOGTrex.enemyPrefab);
            // For different ways of registering your enemy, see https://github.com/EvaisaDev/LethalLib/blob/main/LethalLib/Modules/Enemies.cs
            //Sets the spawn weight per level/moond accordingly if enabled, or uses the default spawnweight for all levels.
            RegisterEnemyWithConfig(BoundConfig.EnableJPOGTrex.Value, BoundConfig.TrexRarity.Value, JPOGTrex, JPOGTrexTN, JPOGTrexTK);
/*            if (BoundConfig.CustomSpawnweightPerLevel.Value == true)
            {
                var JPOGTrexLevelRarities = GetVanillaLevelRarities();
                var JPOGTrexCustomLevelRarities = new Dictionary<string, int>()
                {
                    {"EGyptLevel", 50},
                    {"46 Infernis", 69},    // Either LLL or LE(C) name can be used, LethalLib will handle both

                };
                Enemies.RegisterEnemy(JPOGTrex, JPOGTrexLevelRarities, JPOGTrexCustomLevelRarities, JPOGTrexTN, JPOGTrexTK);
                Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID}: using custom Spawn Weight(s).");
            }
            else
            {
                Enemies.RegisterEnemy(JPOGTrex, BoundConfig.SpawnWeight.Value, Levels.LevelTypes.All, JPOGTrexTN, JPOGTrexTK);
                Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID}: using default Spawn Weight(s).");
            }*/

            Logger.LogInfo($"Plugin [{PluginInfo.PLUGIN_GUID}] is loaded!");
        }

        private static void InitializeNetworkBehaviours() {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

        private static void RegisterEnemyWithConfig(bool isnebabled, string configMoonRarity, EnemyType enemy, TerminalNode terminalNode, TerminalKeyword terminalKeyword)
        {
            if (isnebabled)
            {
                Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID}: JPOGTrex is Enabled, setting up spawn weights");
                (Dictionary<LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) = ConfigParsing(configMoonRarity);
                Enemies.RegisterEnemy(enemy, spawnRateByLevelType, spawnRateByCustomLevelType, terminalNode, terminalKeyword);
            }
            else
            {
                Enemies.RegisterEnemy(enemy, 0, LevelTypes.All, terminalNode, terminalKeyword);
                Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID}: JPOGTrex is set as disabled");
            }
        }

        private static (Dictionary<Levels.LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) ConfigParsing(string configMoonRarity)
        {
            Dictionary<Levels.LevelTypes, int> spawnRateByLevelType = new Dictionary<Levels.LevelTypes, int>();
            Dictionary<string, int> spawnRateByCustomLevelType = new Dictionary<string, int>();
            foreach (string entry in configMoonRarity.Split(',').Select(s => s.Trim()))
            {
                string[] entryParts = entry.Split(':');

                if (entryParts.Length != 2) continue;

                string name = entryParts[0].ToLowerInvariant();
                int spawnrate;

                if (!int.TryParse(entryParts[1], out spawnrate)) continue;
                if (name == "custom")
                {
                    name = "modded";
                }
                if (System.Enum.TryParse(name, true, out Levels.LevelTypes levelType))
                {
                    spawnRateByLevelType[levelType] = spawnrate;
                }
                else
                {
                    // Try appending "Level" to the name and re-attempt parsing
                    string modifiedName = name + "Level";
                    if (System.Enum.TryParse(modifiedName, true, out levelType))
                    {
                        spawnRateByLevelType[levelType] = spawnrate;
                    }
                    else
                    {
                        spawnRateByCustomLevelType[name] = spawnrate;
                    }
                }
            }
            return (spawnRateByLevelType, spawnRateByCustomLevelType);
        }
    }
}