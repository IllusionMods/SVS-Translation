// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
// BepInEx
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
// Unity
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using SV.H;
using SV.H.Words;

[assembly: AssemblyTitle(SVS_Subtitles.SubtitlesPlugin.DisplayName)]
[assembly: AssemblyProduct(SVS_Subtitles.SubtitlesPlugin.DisplayName)]
[assembly: AssemblyVersion(SVS_Subtitles.SubtitlesPlugin.Version)]
[assembly: AssemblyDescription("Show subtitles in H scenes.")]
[assembly: AssemblyCompany("https://github.com/IllusionMods/SVS-Translation")]

namespace SVS_Subtitles;

[BepInPlugin(GUID, DisplayName, Version)]
[BepInDependency("gravydevsupreme.xunity.resourceredirector", BepInDependency.DependencyFlags.SoftDependency)]
public class SubtitlesPlugin : BasePlugin
{
    public const string Version = "0.0.2";
    public const string GUID = "SVS_Subtitles";
    internal const string DisplayName = "SVS Subtitles";

    // BepInEx Config
    private static ConfigEntry<bool> EnableConfig;
    private static ConfigEntry<string> LanguageConfig;

    // Plugin variables
    private static GameObject canvasObject;
    internal static new BepInEx.Logging.ManualLogSource Log;
    private static HScene HSceneInstance;

    private static Dictionary<string, string> subtitleMap;

    public override void Load()
    {
        Log = base.Log;

        // Plugin startup logic
        // Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        EnableConfig = Config.Bind("General", "Enable Subtitles", true, "Reload the game to Enable/Disable.");
        LanguageConfig = Config.Bind("General", "Subtitle Language", "auto", "Language of the subtitles.\nThe subtitles are loaded from 'BepInEx/Translation/<this setting>/SVS_Subtitles.json'.\nIf set to 'auto' or empty, AutoTranslator's Destination Language is used.");

        if (!EnableConfig.Value) return;

        var languageCode = LanguageConfig.Value.Trim();
        if (languageCode == string.Empty || languageCode == "auto")
            languageCode = TranslationHelper.TryGetAutoTranslatorLanguage();

        var subsPath = GetSubtitlesPath(languageCode);

        try
        {
            var jsonString = File.ReadAllText(subsPath);
            subtitleMap = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString) ?? throw new Exception("Failed to deserialize jsonString, could be an empty file");

            Log.LogInfo($"Loaded subtitles from \"{subsPath}\"");
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to load subtitles from \"{subsPath}\" - {e}");
            return;
        }

        Harmony.CreateAndPatchAll(typeof(Hooks));

        // IL2CPP don"t automatically inherits MonoBehaviour, so needs to add a component separatelly
        ClassInjector.RegisterTypeInIl2Cpp<SubtitlesCanvas>();
    }

    private static string GetSubtitlesPath(string? languageCode)
    {
        if (string.IsNullOrEmpty(languageCode))
        {
            Log.LogWarning("AutoTranslator not found or has no language set in its config. Please set the Subtitle Language setting manually or place SVS_Subtitles.json in 'BepInEx/Config'.");
        }
        else
        {
            var subsPath = Path.Combine(Paths.BepInExRootPath, "Translation", languageCode, "SVS_Subtitles.json");
            if (File.Exists(subsPath)) return subsPath;
            Log.LogWarning($"Subtitles file not found at \"{subsPath}\". Looking in config instead...");
        }
        return Path.Combine(Paths.ConfigPath, "SVS_Subtitles.json");
    }

    /// <summary>
    /// Create the subtitle canvas in the desired scene
    /// </summary>
    /// <param name="scene"></param>
    public static void MakeCanvas(Scene scene)
    {
        if (canvasObject)
            UnityEngine.Object.Destroy(canvasObject);

        // Creating Canvas object
        canvasObject = new GameObject("SubtitleCanvas");
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        canvasObject.AddComponent<SubtitlesCanvas>();
    }

    public class SubtitlesCanvas : MonoBehaviour
    {
        // Constructor needed to use Start, Update, etc...
        public SubtitlesCanvas(IntPtr handle) : base(handle) { }

        private static GameObject subtitleObject;
        private static TextMeshProUGUI subtitle;

        public static List<PlayingWord> playingWords = new();

        private static T GetResource<T>(string name) where T : UnityEngine.Object
        {
            var objs = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<T>());
            for (var i = objs.Length - 1; i >= 0; --i)
            {
                var obj = objs[i];
                if (obj.name == name)
                {
                    var ret = obj.TryCast<T>();
                    return ret;
                }
            }
            return null;
        }

        private void Start()
        {
            // Setting canvas attributes
            var canvasScaler = canvasObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(Screen.width, Screen.height);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            canvasObject.AddComponent<CanvasGroup>().blocksRaycasts = false;

            // Setting subtitle object
            subtitleObject = new GameObject("XUAIGNORE SubtitleText");
            subtitleObject.transform.SetParent(canvasObject.transform);

            int fontSize = (int)(Screen.height / 30.0f);

            RectTransform subtitleRect = subtitleObject.AddComponent<RectTransform>();
            subtitleRect.pivot = new Vector2(0, -1);
            subtitleRect.sizeDelta = new Vector2(Screen.width * 0.990f, fontSize + (fontSize * 0.05f));

            subtitle = subtitleObject.AddComponent<TextMeshProUGUI>();
            subtitle.font = GetResource<TMP_FontAsset>("tmp_sv_default");
            subtitle.fontSharedMaterial = GetResource<Material>("tmp_sv_default SVT-10");

            subtitle.fontSize = fontSize;
            subtitle.alignment = TextAlignmentOptions.Bottom;
            subtitle.overflowMode = TextOverflowModes.Overflow;
            subtitle.enableWordWrapping = true;
            subtitle.color = Color.white;
            subtitle.text = "";
        }

        public struct PlayingWord
        {
            public BaseWords baseWords;
            public PlayData.VoiceKind kind;
            public string text;
        }

        // Using Update because coroutines, onDestroy and onDisable are not working as intended
        private void Update()
        {
            if (HSceneInstance && HSceneInstance.isActiveAndEnabled)
            {
                var subtitleText = "";
                playingWords.RemoveAll(word =>
                {
                    var isPlaying = !word.baseWords.WasCollected && (
                        (word.kind == PlayData.VoiceKind.Heart && word.baseWords._player?.IsHeartNow == true)
                        || word.baseWords._player?.IsPlaying(true) == true
                    );

                    if (isPlaying)
                        subtitleText = subtitleText + "\n" + word.text;

                    return !isPlaying;
                });
                if (subtitle.text != subtitleText) subtitle.text = subtitleText;
                if (subtitleText != "") subtitleObject.active = true;
                else subtitleObject.active = false;
            }
            else
            {
                playingWords.Clear();
                subtitleObject.active = false;
            }
        }
    }

    private static class Hooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseWords), nameof(BaseWords.Play))]
        private static void PlayVoice(BaseWords __instance, PlayData data)
        {
            if (data == null || data.Kind == PlayData.VoiceKind.Breath) return;

            if (!EnableConfig.Value) return;

            try
            {
                var voiceAssetFile = data.Voice?.AssetFile;
                var subtitle = subtitleMap.GetValueSafe(voiceAssetFile)
#if DEBUG
                               ?? voiceAssetFile 
#endif
                               ?? "";
                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    Log.LogDebug($"Play voice({voiceAssetFile}): {subtitle}");

                    var actorName = __instance._actor?.Name;
                    if (string.IsNullOrWhiteSpace(actorName))
                        actorName = "???";
                    else if (TranslationHelper.TryTranslate(actorName, out var actorNameTl))
                        actorName = actorNameTl;

                    var text = $"{actorName} 「{subtitle}」";
                    var playingWord = new SubtitlesCanvas.PlayingWord
                    {
                        baseWords = __instance,
                        kind = data.Kind,
                        text = text
                    };

                    SubtitlesCanvas.playingWords.RemoveAll(word => word.baseWords == __instance);
                    SubtitlesCanvas.playingWords.Add(playingWord);
                }
                else
                {
                    Log.LogWarning($"Play voice({voiceAssetFile}): Not in subtitleMap!");
                }
            }
            catch (Exception e)
            {
                Log.LogError($"{__instance} {data} {e}");
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(HScene), nameof(HScene.Start))]
        private static void HSceneInitialize(HScene __instance)
        {
            if (!EnableConfig.Value) return;

            if (HSceneInstance == __instance) return;
            HSceneInstance = __instance;
            MakeCanvas(SceneManager.GetActiveScene());
        }
    }
}
