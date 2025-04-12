using System;
using System.Collections.Generic;

namespace UnityLLMGather
{
    [Serializable]
    public class PatternProfile
    {
        public List<string> ExcludePatterns = new List<string>();
        public List<string> SkipContentPatterns = new List<string>();
        public List<string> IncludePatterns = new List<string>();

        public static PatternProfile CreateDefault()
        {
            return new PatternProfile
            {
                ExcludePatterns = new List<string> {
                    "Library/**", "Temp/**", "Logs/**", "UserSettings/**",
                    "obj/**", "Build/**", "*.csproj", "*.sln",
                    "*.userprefs", "*.suo", "*.meta"
                },
                SkipContentPatterns = new List<string> {
                    "*.dll", "*.asset", "*.prefab", "*.unity", "*.png", "*.jpg", "*.tga", "*.psd", // 一般的なバイナリ/アセット
                    "*.fbx", "*.obj", "*.blend", "*.max", "*.ma", // 3Dモデル
                    "*.anim", "*.controller", "*.mat", "*.spriteatlas", // アニメーション、マテリアルなど
                    "*.ttf", "*.otf", // フォント
                    "*.mp3", "*.wav", "*.ogg" // オーディオ
                },
                IncludePatterns = new List<string>()
            };
        }
    }

    [Serializable]
    public class GathererSettingsData
    {
        public List<ProfileNameKeyPair> Profiles = new List<ProfileNameKeyPair>();
        public string SelectedProfileName = GathererSettings.DefaultProfileName;
        public List<string> IgnoreSceneObjectNames = new List<string>();
    }

    [Serializable]
    public class ProfileNameKeyPair
    {
        public string Name;
        public PatternProfile Profile;
    }
}