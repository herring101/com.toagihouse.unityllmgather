using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UnityLLMGather
{
    public class UnityLLMGatherWindow : EditorWindow
    {
        private string targetDirectory = "Assets";
        private string outputFileName = "ProjectSummary.md";
        private int maxLinesPerFile = 1000;
        private long maxFileSize = 0;
        private bool includeSceneSummary = true;
        private bool openAfterGenerate = true;

        private GathererSettingsData settingsData;
        private Dictionary<string, PatternProfile> patternProfiles => GathererSettings.GetProfilesDict(settingsData);
        private List<string> profileNames;
        private int selectedProfileIndex = 0;
        private string newProfileNameInput = "";
        private string renamingProfileName = null;
        private string renameProfileNewNameInput = "";

        private ReorderableList excludeList;
        private ReorderableList skipContentList;
        private ReorderableList includeList;
        private ReorderableList ignoreSceneObjectList;

        private Vector2 scrollPosition;
        private bool generalSettingsFoldout = true;
        private bool patternSettingsFoldout = true;
        private bool sceneSettingsFoldout = true;
        private bool advancedSettingsFoldout = false;

        private const string PrefsPrefix = "ULG_"; // Unity LLM Gather

        [MenuItem("Tools/Unity LLM Gather")]
        public static void ShowWindow()
        {
            GetWindow<UnityLLMGatherWindow>("Unity LLM Gather");
        }

        private void OnEnable()
        {
            LoadSettingsAndSetupUI();
        }

        private void LoadSettingsAndSetupUI()
        {
            settingsData = GathererSettings.LoadSettings();
            profileNames = settingsData.Profiles.Select(p => p.Name).ToList();

            selectedProfileIndex = profileNames.IndexOf(settingsData.SelectedProfileName);
            if (selectedProfileIndex < 0) selectedProfileIndex = 0;

            targetDirectory = EditorPrefs.GetString(PrefsPrefix + "TargetDirectory", "Assets");
            outputFileName = EditorPrefs.GetString(PrefsPrefix + "OutputFileName", "ProjectSummary.md");
            maxLinesPerFile = EditorPrefs.GetInt(PrefsPrefix + "MaxLinesPerFile", 1000);
            maxFileSize = long.Parse(EditorPrefs.GetString(PrefsPrefix + "MaxFileSize", "0"));
            includeSceneSummary = EditorPrefs.GetBool(PrefsPrefix + "IncludeSceneSummary", true);
            openAfterGenerate = EditorPrefs.GetBool(PrefsPrefix + "OpenAfterGenerate", true);

            SetupReorderableLists();
            LoadCurrentProfileDataToLists();
        }

        private void OnDisable()
        {
            UpdateCurrentProfileFromLists();
            SaveUISettingsToPrefs();
            settingsData.SelectedProfileName = profileNames[selectedProfileIndex];
            GathererSettings.SaveSettings(settingsData);
        }

        private void SaveUISettingsToPrefs()
        {
            EditorPrefs.SetString(PrefsPrefix + "TargetDirectory", targetDirectory);
            EditorPrefs.SetString(PrefsPrefix + "OutputFileName", outputFileName);
            EditorPrefs.SetInt(PrefsPrefix + "MaxLinesPerFile", maxLinesPerFile);
            EditorPrefs.SetString(PrefsPrefix + "MaxFileSize", maxFileSize.ToString());
            EditorPrefs.SetBool(PrefsPrefix + "IncludeSceneSummary", includeSceneSummary);
            EditorPrefs.SetBool(PrefsPrefix + "OpenAfterGenerate", openAfterGenerate);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUI.BeginChangeCheck();

            DrawGeneralSettings();
            DrawPatternSettings();
            DrawSceneSettings();
            DrawAdvancedSettings();

            GUILayout.Space(20);
            DrawActionButtons();

            if (EditorGUI.EndChangeCheck() && renamingProfileName == null)
            {
                UpdateCurrentProfileFromLists();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawGeneralSettings()
        {
            generalSettingsFoldout = EditorGUILayout.Foldout(generalSettingsFoldout, "Basic Settings", true, EditorStyles.foldoutHeader);
            if (!generalSettingsFoldout) return;

            targetDirectory = EditorGUILayout.TextField(new GUIContent("Target Directory", "Relative path from project root"), targetDirectory);
            outputFileName = EditorGUILayout.TextField(new GUIContent("Output File Name"), outputFileName);
            maxLinesPerFile = EditorGUILayout.IntField(new GUIContent("Max Lines Per File", "Lines exceeding this limit will be truncated"), maxLinesPerFile);
            maxFileSize = EditorGUILayout.LongField(new GUIContent("Max File Size (bytes)", "0 for no limit. Files exceeding this size will have their content skipped"), maxFileSize);
            openAfterGenerate = EditorGUILayout.Toggle(new GUIContent("Open File After Generation", "If checked, the output file will be opened with the associated application upon completion"), openAfterGenerate);
            EditorGUILayout.Space();
        }

        private void DrawPatternSettings()
        {
            patternSettingsFoldout = EditorGUILayout.Foldout(patternSettingsFoldout, "File Filtering Settings", true, EditorStyles.foldoutHeader);
            if (!patternSettingsFoldout) return;

            DrawProfileManagement();

            using (new EditorGUI.DisabledScope(renamingProfileName != null))
            {
                EditorGUILayout.LabelField("Exclude Patterns (Glob recommended)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Examples: `*.log`, `**/Temp/`, `Library/**`. Matching files/directories will be excluded from the summary.", MessageType.Info);
                excludeList?.DoLayoutList();

                EditorGUILayout.LabelField("Skip Content Patterns (Glob recommended)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Examples: `*.dll`, `*.asset`, `**/data.json`. Matching files will appear in the structure but content will be shown as '(Skipped)'.", MessageType.Info);
                skipContentList?.DoLayoutList();

                EditorGUILayout.LabelField("Include Patterns (Glob recommended, Empty means All)", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Examples: `*.cs`, `*.shader`, `Assets/Scripts/**`. If specified, only matching files will be included.", MessageType.Info);
                includeList?.DoLayoutList();
            }
            EditorGUILayout.Space();
        }

        private void DrawSceneSettings()
        {
            sceneSettingsFoldout = EditorGUILayout.Foldout(sceneSettingsFoldout, "Scene Summary Settings", true, EditorStyles.foldoutHeader);
            if (!sceneSettingsFoldout) return;

            includeSceneSummary = EditorGUILayout.Toggle(new GUIContent("Include Scene Summary", "If checked, the structure and object info of the currently active scene will be included"), includeSceneSummary);

            using (new EditorGUI.DisabledScope(!includeSceneSummary || renamingProfileName != null))
            {
                EditorGUILayout.LabelField("Ignore GameObject Names", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("GameObjects with names added here will be excluded from the scene summary.", MessageType.Info);
                ignoreSceneObjectList?.DoLayoutList();
            }
            EditorGUILayout.Space();
        }

        private void DrawAdvancedSettings() { /* Placeholder for future settings */ }

        private void DrawActionButtons()
        {
            using (new EditorGUI.DisabledScope(renamingProfileName != null))
            {
                if (GUILayout.Button("Generate Summary", GUILayout.Height(30)))
                {
                    TriggerSummaryGeneration();
                }
            }
        }

        private void DrawProfileManagement()
        {
            GUILayout.Label("Pattern Profiles", EditorStyles.boldLabel);

            if (renamingProfileName != null)
            {
                EditorGUILayout.LabelField($"New name for profile '{renamingProfileName}':");
                renameProfileNewNameInput = EditorGUILayout.TextField(renameProfileNewNameInput);
                EditorGUILayout.BeginHorizontal();
                bool invalidNewName = string.IsNullOrWhiteSpace(renameProfileNewNameInput) || renameProfileNewNameInput == renamingProfileName || profileNames.Contains(renameProfileNewNameInput);
                using (new EditorGUI.DisabledScope(invalidNewName))
                {
                    if (GUILayout.Button("Confirm"))
                    {
                        ConfirmRenameProfile(renamingProfileName, renameProfileNewNameInput);
                        renamingProfileName = null;
                    }
                }
                if (GUILayout.Button("Cancel"))
                {
                    renamingProfileName = null;
                    renameProfileNewNameInput = "";
                }
                EditorGUILayout.EndHorizontal();
                if (invalidNewName && !string.IsNullOrWhiteSpace(renameProfileNewNameInput))
                {
                    EditorGUILayout.HelpBox("Name cannot be empty, unchanged, or already exist.", MessageType.Warning);
                }
            }
            else
            {
                int newSelectedProfileIndex = EditorGUILayout.Popup("Selected Profile", selectedProfileIndex, profileNames.ToArray());
                if (newSelectedProfileIndex != selectedProfileIndex)
                {
                    UpdateCurrentProfileFromLists();
                    selectedProfileIndex = newSelectedProfileIndex;
                    settingsData.SelectedProfileName = profileNames[selectedProfileIndex];
                    LoadCurrentProfileDataToLists();
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Save Current Profile", "Overwrite the selected profile with the current settings")))
                {
                    SaveChangesToSelectedProfile();
                }
                using (new EditorGUI.DisabledScope(profileNames[selectedProfileIndex] == GathererSettings.DefaultProfileName))
                {
                    if (GUILayout.Button(new GUIContent("Delete Selected Profile", "Delete the currently selected profile")))
                    {
                        DeleteSelectedProfile();
                    }
                    if (GUILayout.Button(new GUIContent("Rename", "Rename the selected profile")))
                    {
                        renamingProfileName = profileNames[selectedProfileIndex];
                        renameProfileNewNameInput = renamingProfileName;
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                newProfileNameInput = EditorGUILayout.TextField("New Profile Name", newProfileNameInput);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newProfileNameInput) || profileNames.Contains(newProfileNameInput)))
                {
                    if (GUILayout.Button("Save as New Profile"))
                    {
                        CreateNewProfile(newProfileNameInput);
                        newProfileNameInput = "";
                        GUI.FocusControl(null);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
        }


        private void SetupReorderableLists()
        {
            var currentProfile = GetCurrentProfileData();
            excludeList = CreatePatternList(currentProfile.ExcludePatterns, "Exclude Patterns");
            skipContentList = CreatePatternList(currentProfile.SkipContentPatterns, "Skip Content Patterns");
            includeList = CreatePatternList(currentProfile.IncludePatterns, "Include Patterns");

            if (settingsData.IgnoreSceneObjectNames == null) settingsData.IgnoreSceneObjectNames = new List<string>();
            ignoreSceneObjectList = CreatePatternList(settingsData.IgnoreSceneObjectNames, "Ignore GameObject Names");
        }

        private ReorderableList CreatePatternList(List<string> targetList, string headerText)
        {
            if (targetList == null) targetList = new List<string>();

            ReorderableList list = null;
            list = new ReorderableList(targetList, typeof(string), true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, headerText),
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;
                    rect.height = EditorGUIUtility.singleLineHeight;
                    if (list != null && list.list != null && index >= 0 && index < list.list.Count)
                    {
                        list.list[index] = EditorGUI.TextField(rect, list.list[index] as string ?? "");
                    }
                    else { EditorGUI.LabelField(rect, "Error: Invalid List/Index"); }
                },
                onAddCallback = l => l.list.Add(""),
            };
            return list;
        }

        private PatternProfile GetCurrentProfileData()
        {
            string currentName = (profileNames != null && profileNames.Count > selectedProfileIndex && selectedProfileIndex >= 0)
                               ? profileNames[selectedProfileIndex]
                               : GathererSettings.DefaultProfileName;

            var pair = settingsData.Profiles.FirstOrDefault(p => p.Name == currentName);
            if (pair != null)
            {
                if (pair.Profile == null) pair.Profile = new PatternProfile();
                return pair.Profile;
            }
            else
            {
                Debug.LogError($"Current profile '{currentName}' not found in settings data! Falling back to default.");
                pair = settingsData.Profiles.FirstOrDefault(p => p.Name == GathererSettings.DefaultProfileName);
                if (pair == null) // Should not happen if LoadSettings works correctly
                {
                    var defaultProfile = PatternProfile.CreateDefault();
                    settingsData.Profiles.Insert(0, new ProfileNameKeyPair { Name = GathererSettings.DefaultProfileName, Profile = defaultProfile });
                    if (!profileNames.Contains(GathererSettings.DefaultProfileName)) profileNames.Insert(0, GathererSettings.DefaultProfileName);
                    selectedProfileIndex = 0;
                    settingsData.SelectedProfileName = GathererSettings.DefaultProfileName;
                    return defaultProfile;
                }
                if (pair.Profile == null) pair.Profile = PatternProfile.CreateDefault();
                selectedProfileIndex = profileNames.IndexOf(GathererSettings.DefaultProfileName);
                if (selectedProfileIndex < 0) selectedProfileIndex = 0;
                settingsData.SelectedProfileName = GathererSettings.DefaultProfileName;
                return pair.Profile;
            }
        }

        private void UpdateCurrentProfileFromLists()
        {
            var currentProfileData = GetCurrentProfileData();
            if (currentProfileData == null) return;

            currentProfileData.ExcludePatterns = excludeList?.list.Cast<string>().ToList() ?? new List<string>();
            currentProfileData.SkipContentPatterns = skipContentList?.list.Cast<string>().ToList() ?? new List<string>();
            currentProfileData.IncludePatterns = includeList?.list.Cast<string>().ToList() ?? new List<string>();

            settingsData.IgnoreSceneObjectNames = ignoreSceneObjectList?.list.Cast<string>().ToList() ?? new List<string>();
        }


        private void LoadCurrentProfileDataToLists()
        {
            var currentProfileData = GetCurrentProfileData();
            if (currentProfileData == null) return;

            if (currentProfileData.ExcludePatterns == null) currentProfileData.ExcludePatterns = new List<string>();
            if (currentProfileData.SkipContentPatterns == null) currentProfileData.SkipContentPatterns = new List<string>();
            if (currentProfileData.IncludePatterns == null) currentProfileData.IncludePatterns = new List<string>();
            if (settingsData.IgnoreSceneObjectNames == null) settingsData.IgnoreSceneObjectNames = new List<string>();

            excludeList = CreatePatternList(currentProfileData.ExcludePatterns, "Exclude Patterns");
            skipContentList = CreatePatternList(currentProfileData.SkipContentPatterns, "Skip Content Patterns");
            includeList = CreatePatternList(currentProfileData.IncludePatterns, "Include Patterns");
            ignoreSceneObjectList = CreatePatternList(settingsData.IgnoreSceneObjectNames, "Ignore GameObject Names");

            Repaint();
        }

        private void SaveChangesToSelectedProfile()
        {
            UpdateCurrentProfileFromLists();
            settingsData.SelectedProfileName = profileNames[selectedProfileIndex];
            GathererSettings.SaveSettings(settingsData);
            ShowNotification(new GUIContent("Profile saved"));
        }

        private void CreateNewProfile(string name)
        {
            UpdateCurrentProfileFromLists();
            var currentProfile = GetCurrentProfileData();

            var newProfileData = new PatternProfile
            {
                ExcludePatterns = new List<string>(currentProfile.ExcludePatterns),
                SkipContentPatterns = new List<string>(currentProfile.SkipContentPatterns),
                IncludePatterns = new List<string>(currentProfile.IncludePatterns)
            };

            settingsData.Profiles.Add(new ProfileNameKeyPair { Name = name, Profile = newProfileData });
            profileNames.Add(name);
            selectedProfileIndex = profileNames.Count - 1;
            settingsData.SelectedProfileName = name;

            GathererSettings.SaveSettings(settingsData);
            LoadCurrentProfileDataToLists();
            ShowNotification(new GUIContent($"Profile '{name}' created"));
        }

        private void DeleteSelectedProfile()
        {
            string nameToDelete = profileNames[selectedProfileIndex];
            if (nameToDelete == GathererSettings.DefaultProfileName) return;

            if (EditorUtility.DisplayDialog("Confirm Profile Deletion", $"Are you sure you want to delete the profile '{nameToDelete}'?", "Delete", "Cancel"))
            {
                settingsData.Profiles.RemoveAll(p => p.Name == nameToDelete);
                profileNames.RemoveAt(selectedProfileIndex);
                selectedProfileIndex = Mathf.Max(0, selectedProfileIndex - 1);
                settingsData.SelectedProfileName = profileNames[selectedProfileIndex];

                GathererSettings.SaveSettings(settingsData);
                LoadCurrentProfileDataToLists();
                ShowNotification(new GUIContent($"Profile '{nameToDelete}' deleted"));
            }
        }

        private void ConfirmRenameProfile(string oldName, string newName)
        {
            var profilePair = settingsData.Profiles.FirstOrDefault(p => p.Name == oldName);
            if (profilePair != null)
            {
                profilePair.Name = newName;
                profileNames[selectedProfileIndex] = newName;
                settingsData.SelectedProfileName = newName;

                GathererSettings.SaveSettings(settingsData);
                Repaint();
                ShowNotification(new GUIContent($"Profile renamed from '{oldName}' to '{newName}'"));
            }
            else
            {
                Debug.LogError($"Rename failed: Profile '{oldName}' not found in data.");
            }
        }

        private void TriggerSummaryGeneration()
        {
            UpdateCurrentProfileFromLists();
            var currentProfileData = GetCurrentProfileData();

            var logic = new GathererLogic(
                targetDirectory,
                maxLinesPerFile,
                maxFileSize,
                includeSceneSummary,
                currentProfileData,
                settingsData.IgnoreSceneObjectNames
            );

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            // Ensure targetDirectory is handled correctly (relative to project root)
            string fullTargetPath = Path.Combine(projectRoot, targetDirectory);
            if (!Directory.Exists(fullTargetPath) && !File.Exists(fullTargetPath))
            {
                if (targetDirectory == "Assets" && Directory.Exists(Application.dataPath))
                {
                    // Common case: Target is Assets, use Application.dataPath
                    fullTargetPath = Application.dataPath;
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", $"Target directory or file not found: {targetDirectory}\nFull Path: {fullTargetPath}", "OK");
                    return;
                }
            }


            string outputPath = Path.Combine(projectRoot, outputFileName);

            try
            {
                System.Action<string, float> updateProgress = (info, progress) =>
                {
                    EditorUtility.DisplayProgressBar("Generating Summary", info, progress);
                };

                List<string> outputLines = logic.GenerateSummary(updateProgress);

                updateProgress("Writing to file...", 0.95f);
                File.WriteAllText(outputPath, string.Join("\n", outputLines), System.Text.Encoding.UTF8);
                AssetDatabase.Refresh();

                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Summary Generation Complete", $"Summary generated: {outputPath}", "OK");

                Debug.Log($"[UnityLLMGather] Summary Generation Complete: {outputPath}\n" +
                          $"Total Files Found: {logic.TotalFilesFound}\n" +
                          $"Files Processed (Content Included): {logic.ProcessedFiles}\n" +
                          $"Skipped by Include Pattern: {logic.SkippedFilesInclude}\n" +
                          $"Skipped by Size Limit: {logic.SkippedFilesSize}\n" +
                          $"Skipped by Skip Content Pattern: {logic.SkippedFilesContent}\n" +
                          $"Skipped as Binary: {logic.SkippedFilesBinary}\n" +
                          $"Scene Summary Included: {includeSceneSummary}");

                if (openAfterGenerate) OpenFile(outputPath);
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[UnityLLMGather] Error during summary generation: {e}");
                EditorUtility.DisplayDialog("Error", $"An error occurred during summary generation:\n{e.Message}", "OK");
            }
        }

        private void OpenFile(string path)
        {
            try
            {
                // Ensure the path uses correct separators for the OS before opening
                string osPath = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                EditorUtility.OpenWithDefaultApp(osPath);
            }
            catch (System.Exception e) { Debug.LogError($"[UnityLLMGather] Failed to open file: {path} - {e.Message}"); }
        }
    }
}