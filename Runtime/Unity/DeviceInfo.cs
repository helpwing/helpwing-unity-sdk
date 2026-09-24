using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Helpwing
{
    /// <summary>What the device says about itself: night mode, language and time zone.</summary>
    public static class DeviceInfo
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool _HelpwingPrefersDark();
#endif

        /// <summary>Whether the OS is in dark mode. The editor answers with its own skin.</summary>
        public static bool PrefersDark()
        {
            try
            {
#if UNITY_EDITOR
                return UnityEditor.EditorGUIUtility.isProSkin;
#elif UNITY_IOS
                return _HelpwingPrefersDark();
#elif UNITY_ANDROID
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                using (var configuration = resources.Call<AndroidJavaObject>("getConfiguration"))
                {
                    const int UiModeNightMask = 0x30;
                    const int UiModeNightYes = 0x20;
                    return (configuration.Get<int>("uiMode") & UiModeNightMask) == UiModeNightYes;
                }
#else
                return false;
#endif
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The device language as a tag Copy.Match understands, e.g. <c>ru</c>.</summary>
        public static string Locale()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Afrikaans: return "af";
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Basque: return "eu";
                case SystemLanguage.Belarusian: return "be";
                case SystemLanguage.Bulgarian: return "bg";
                case SystemLanguage.Catalan: return "ca";
                case SystemLanguage.Chinese: return "zh";
                case SystemLanguage.ChineseSimplified: return "zh-hans";
                case SystemLanguage.ChineseTraditional: return "zh-hant";
                case SystemLanguage.Czech: return "cs";
                case SystemLanguage.Danish: return "da";
                case SystemLanguage.Dutch: return "nl";
                case SystemLanguage.English: return "en";
                case SystemLanguage.Estonian: return "et";
                case SystemLanguage.Faroese: return "fo";
                case SystemLanguage.Finnish: return "fi";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.German: return "de";
                case SystemLanguage.Greek: return "el";
                case SystemLanguage.Hebrew: return "he";
                case SystemLanguage.Hungarian: return "hu";
                case SystemLanguage.Icelandic: return "is";
                case SystemLanguage.Indonesian: return "id";
                case SystemLanguage.Italian: return "it";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Latvian: return "lv";
                case SystemLanguage.Lithuanian: return "lt";
                case SystemLanguage.Norwegian: return "no";
                case SystemLanguage.Polish: return "pl";
                case SystemLanguage.Portuguese: return "pt";
                case SystemLanguage.Romanian: return "ro";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.SerboCroatian: return "sh";
                case SystemLanguage.Slovak: return "sk";
                case SystemLanguage.Slovenian: return "sl";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.Swedish: return "sv";
                case SystemLanguage.Thai: return "th";
                case SystemLanguage.Turkish: return "tr";
                case SystemLanguage.Ukrainian: return "uk";
                case SystemLanguage.Vietnamese: return "vi";
                default: return "";
            }
        }

        /// <summary>The local time zone id, for the agent's benefit. IANA on iOS and Android.</summary>
        public static string Timezone()
        {
            try
            {
                return TimeZoneInfo.Local.Id ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
