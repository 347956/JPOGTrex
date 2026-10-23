using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace JPOGTrex.Configuration {
    public class PluginConfig
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
        // Spawning is configured through DawnLib, these are the T-Rex's own behaviour settings.
        public ConfigEntry<float> DefaultSpeed;
        public ConfigEntry<int> VisionRangeLength;
        public ConfigEntry<int> VisionRangeWidth;
        public ConfigEntry<float> SuspiciontDecreaseTime;
        public ConfigEntry<int> MaxSuspicionLevel;
        public ConfigEntry<int> SuspicionIncrement;
        public ConfigEntry<int> SuspicionDecrement;

        private const string CATEGORY_BEHAVIOR = "Behavior";

        public PluginConfig(ConfigFile cfg)
        {
            // The value ranges keep the T-Rex working with odd values, e.g. the chase vision range is the vision length - 20
            DefaultSpeed = cfg.Bind(CATEGORY_BEHAVIOR, "Default speed", 6f, new ConfigDescription(
                "The default speed of the T-Rex. Increasing the default speed will also increase the chasing speed e.g. default speed * 2 = chasing speed",
                new AcceptableValueRange<float>(1f, 20f)));

            VisionRangeLength = cfg.Bind(CATEGORY_BEHAVIOR, "Vision Range length", 70, new ConfigDescription(
                "Increasing this number will make the T-Rex detect players further away.",
                new AcceptableValueRange<int>(20, 300)));

            VisionRangeWidth = cfg.Bind(CATEGORY_BEHAVIOR, "Vision Range Width", 60, new ConfigDescription(
                "Increasing this number will broaden the width of the T-Rex's vision. \n" +
                "WARNING: increasing this value too much can result in the T-rex being able to look behind itself.",
                new AcceptableValueRange<int>(10, 180)));

            MaxSuspicionLevel = cfg.Bind(CATEGORY_BEHAVIOR, "Max Suspicion Level", 100, new ConfigDescription(
                "The max level/amount of suspicion the T-Rex needs before initiating a chase",
                new AcceptableValueRange<int>(1, 1000)));

            SuspicionIncrement = cfg.Bind(CATEGORY_BEHAVIOR, "Suspicion Increment Amount", 10, new ConfigDescription(
                "The amount by which the suspicion is increased when the T-Rex sees a player moving",
                new AcceptableValueRange<int>(1, 1000)));

            SuspicionDecrement = cfg.Bind(CATEGORY_BEHAVIOR, "Suspicion Decrement Amount", 5, new ConfigDescription(
                "The amount by which the suspicion is decreased when the T-Rex doesn't see players moving.",
                new AcceptableValueRange<int>(0, 1000)));

            SuspiciontDecreaseTime = cfg.Bind(CATEGORY_BEHAVIOR, "Suspicion Decrease Time", 4f, new ConfigDescription(
                "Time in seconds. The interval between decreasing suspicion.\n" +
                "e.g. 4 means the suspicion will go down by the set amount every 4 seconds since the T-Rex last spotted a player moving.",
                new AcceptableValueRange<float>(0.1f, 60f)));

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg) {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType().GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries = (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }
    }
}
