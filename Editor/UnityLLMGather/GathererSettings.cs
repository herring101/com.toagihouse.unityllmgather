using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityLLMGather
{
    public static class GathererSettings
    {
        public const string DefaultProfileName = "Default"; // Changed "デフォルト" to "Default"
        private const string SettingsFileName = "LLMGatherProfiles.json"; // Renamed file
        private static string SettingsFilePath => Path.Combine("ProjectSettings", SettingsFileName);

        private static GathererSettingsData cachedSettings = null;

        public static GathererSettingsData LoadSettings()
        {
            if (cachedSettings != null)
            {
                return cachedSettings;
            }

            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    cachedSettings = JsonUtility.FromJson<GathererSettingsData>(json);

                    if (cachedSettings == null)
                    {
                        Debug.LogWarning($"[UnityLLMGather] Settings file '{SettingsFilePath}' might be corrupted. Creating default settings.");
                        cachedSettings = CreateDefaultSettings();
                        SaveSettings(cachedSettings);
                    }
                    else if (cachedSettings.Profiles == null || !cachedSettings.Profiles.Any(p => p.Name == DefaultProfileName))
                    {
                        if (cachedSettings.Profiles == null) cachedSettings.Profiles = new List<ProfileNameKeyPair>();
                        cachedSettings.Profiles.Insert(0, new ProfileNameKeyPair { Name = DefaultProfileName, Profile = PatternProfile.CreateDefault() });
                        if (string.IsNullOrEmpty(cachedSettings.SelectedProfileName) || !cachedSettings.Profiles.Any(p => p.Name == cachedSettings.SelectedProfileName))
                        {
                            cachedSettings.SelectedProfileName = DefaultProfileName;
                        }
                        SaveSettings(cachedSettings);
                    }
                    // Ensure IgnoreSceneObjectNames is initialized even if file exists but lacks the field
                    if (cachedSettings.IgnoreSceneObjectNames == null)
                    {
                        cachedSettings.IgnoreSceneObjectNames = new List<string>();
                        // Optionally save immediately if we had to initialize this list
                        // SaveSettings(cachedSettings);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[UnityLLMGather] Failed to load settings from '{SettingsFilePath}': {e}. Creating default settings.");
                    cachedSettings = CreateDefaultSettings();
                    SaveSettings(cachedSettings);
                }
            }
            else
            {
                Debug.Log($"[UnityLLMGather] Settings file not found at '{SettingsFilePath}'. Creating default settings file.");
                cachedSettings = CreateDefaultSettings();
                SaveSettings(cachedSettings);
            }

            // Final null checks just in case
            if (cachedSettings.Profiles == null) cachedSettings.Profiles = new List<ProfileNameKeyPair>();
            if (cachedSettings.IgnoreSceneObjectNames == null) cachedSettings.IgnoreSceneObjectNames = new List<string>();

            return cachedSettings;
        }

        public static void SaveSettings(GathererSettingsData settings)
        {
            try
            {
                cachedSettings = settings;

                string directory = Path.GetDirectoryName(SettingsFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Ensure Profiles and IgnoreSceneObjectNames are not null before saving
                if (cachedSettings.Profiles == null) cachedSettings.Profiles = new List<ProfileNameKeyPair>();
                if (cachedSettings.IgnoreSceneObjectNames == null) cachedSettings.IgnoreSceneObjectNames = new List<string>();
                // Ensure default profile exists before saving? LoadSettings should handle this.

                string json = JsonUtility.ToJson(settings, true);
                File.WriteAllText(SettingsFilePath, json);
                // AssetDatabase.Refresh(); // Not typically needed for ProjectSettings
                Debug.Log($"[UnityLLMGather] Settings saved to '{SettingsFilePath}'.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[UnityLLMGather] Failed to save settings to '{SettingsFilePath}': {e}");
            }
        }

        private static GathererSettingsData CreateDefaultSettings()
        {
            var defaultSettings = new GathererSettingsData
            {
                Profiles = new List<ProfileNameKeyPair>
                {
                    new ProfileNameKeyPair
                    {
                        Name = DefaultProfileName,
                        Profile = PatternProfile.CreateDefault()
                    }
                },
                SelectedProfileName = DefaultProfileName,
                IgnoreSceneObjectNames = new List<string>() // Initialize as empty list
            };
            return defaultSettings;
        }

        public static Dictionary<string, PatternProfile> GetProfilesDict(GathererSettingsData settings)
        {
            if (settings?.Profiles == null) return new Dictionary<string, PatternProfile>();
            // Handle potential duplicate keys if file gets corrupted manually, although UI prevents this.
            try
            {
                return settings.Profiles.ToDictionary(p => p.Name, p => p.Profile);
            }
            catch (System.ArgumentException ex)
            {
                Debug.LogError($"[UnityLLMGather] Duplicate profile names detected in settings data. Please check '{SettingsFilePath}'. Error: {ex.Message}");
                // Return a dictionary with duplicates removed (first wins)
                var dict = new Dictionary<string, PatternProfile>();
                foreach (var pair in settings.Profiles)
                {
                    if (!dict.ContainsKey(pair.Name))
                    {
                        dict.Add(pair.Name, pair.Profile);
                    }
                }
                // Correct the loaded data?
                // settings.Profiles = dict.Select(kvp => new ProfileNameKeyPair { Name = kvp.Key, Profile = kvp.Value }).ToList();
                // SaveSettings(settings); // Save corrected data? Risky without user confirmation.
                return dict;
            }
        }

        public static void SetProfilesFromDict(GathererSettingsData settings, Dictionary<string, PatternProfile> dict)
        {
            if (settings == null || dict == null) return;
            settings.Profiles = dict.Select(kvp => new ProfileNameKeyPair { Name = kvp.Key, Profile = kvp.Value }).ToList();
        }
    }
}