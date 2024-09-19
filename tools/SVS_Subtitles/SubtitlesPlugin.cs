// System
using System;
using System.Collections.Generic;
using System.IO;
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
using System.Reflection;

[assembly: AssemblyTitle(SVS_Subtitles.SubtitlesPlugin.DisplayName)]
[assembly: AssemblyProduct(SVS_Subtitles.SubtitlesPlugin.DisplayName)]
[assembly: AssemblyVersion(SVS_Subtitles.SubtitlesPlugin.Version)]
[assembly: AssemblyDescription("Show subtitles in H scenes.")]
[assembly: AssemblyCompany("ekibun")]

namespace SVS_Subtitles;

[BepInPlugin(GUID, DisplayName, Version)]
public class SubtitlesPlugin : BasePlugin
{
    public const string Version = "0.0.1";
    public const string GUID = "SVS_Subtitles";
    internal const string DisplayName = "SVS Subtitles";

    // BepInEx Config
    internal static ConfigEntry<bool> EnableConfig;

    // Plugin variables
    static GameObject canvasObject;
    internal static new BepInEx.Logging.ManualLogSource Log;
    internal static HScene HSceneInstance;

    internal static Dictionary<string, string> subtitleMap;
    public override void Load()
    {
        Log = base.Log;

        // Plugin startup logic
        // Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        EnableConfig = Config.Bind("General",
                                         "Enable Subtitles",
                                         true,
                                         "Reload the game to Enable/Disable");

        if (EnableConfig.Value)
        {
            var jsonString = File.ReadAllText(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "SVS_Subtitles.json"));
            subtitleMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
            Harmony.CreateAndPatchAll(typeof(Hooks));
        }

        // IL2CPP don"t automatically inherits MonoBehaviour, so needs to add a component separatelly
        ClassInjector.RegisterTypeInIl2Cpp<SubtitlesCanvas>();
    }

    /// <summary>
    /// Create the subtitle canvas in the desired scene
    /// </summary>
    /// <param name="scene"></param>
    public static void MakeCanvas(Scene scene)
    {
        if (canvasObject != null)
        {
            GameObject.Destroy(canvasObject);
        }
        // Creating Canvas object
        canvasObject = new GameObject("SubtitleCanvas");
        SceneManager.MoveGameObjectToScene(canvasObject, scene);
        canvasObject.AddComponent<SubtitlesCanvas>();
    }

    public class SubtitlesCanvas : MonoBehaviour
    {
        // Constructor needed to use Start, Update, etc...
        public SubtitlesCanvas(IntPtr handle) : base(handle) { }

        static GameObject subtitleObject;
        static TextMeshProUGUI subtitle;

        public static List<PlayingWord> playingWords = new();

        static T getResource<T>(string name) where T : UnityEngine.Object
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

        void Start()
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
            subtitle.font = getResource<TMP_FontAsset>("tmp_sv_default");
            subtitle.fontSharedMaterial = getResource<Material>("tmp_sv_default SVT-10");

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
        void Update()
        {
            if (HSceneInstance != null && HSceneInstance?.isActiveAndEnabled == true)
            {
                var subtitleText = "";
                playingWords.RemoveAll((PlayingWord word) =>
                {
                    var isPlaying = !word.baseWords.WasCollected && (
                        (word.kind == PlayData.VoiceKind.Heart && word.baseWords._player?.IsHeartNow == true)
                        || word.baseWords._player?.IsPlaying(true) == true 
                    );
                    
                    if (isPlaying)
                    {
                        subtitleText = subtitleText + "\n" + word.text;
                    }
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

    internal static class Hooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseWords), nameof(BaseWords.Play))]
        private static void PlayVoice(BaseWords __instance, PlayData data)
        {
            if (data.Kind == PlayData.VoiceKind.Breath) return;
            var subtitle = subtitleMap.GetValueSafe(data.Voice?.AssetFile) ?? data.Voice?.AssetFile ?? "";
            var text = $"{__instance._actor?.Name} 「{subtitle}」";
            Log.LogInfo($"Play voice({data.Voice?.AssetFile}): {subtitle}");
            if (!subtitle.IsNullOrEmpty())
            {
                var playingWord = new SubtitlesCanvas.PlayingWord()
                {
                    baseWords = __instance,
                    kind = data.Kind,
                    text = text
                };
                SubtitlesCanvas.playingWords.RemoveAll((SubtitlesCanvas.PlayingWord word) =>
                {
                    return word.baseWords == __instance;
                });
                SubtitlesCanvas.playingWords.Add(playingWord);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(HScene), nameof(HScene.Start))]
        private static void HSceneInitialize(HScene __instance)
        {
            if (HSceneInstance == __instance) return;
            HSceneInstance = __instance;
            MakeCanvas(SceneManager.GetActiveScene());
        }

    }
}
