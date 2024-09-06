using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ILLGames.Component.UI;
using TextureTrigger;
using UnityEngine.UI;

[assembly: AssemblyTitle(TextureTriggerPlugin.DisplayName)]
[assembly: AssemblyProduct(TextureTriggerPlugin.DisplayName)]
[assembly: AssemblyDescription("Workaround for some images not getting translated by AutoTranslator.")]
[assembly: AssemblyVersion(TextureTriggerPlugin.Version)]

namespace TextureTrigger
{
    [BepInPlugin(GUID, DisplayName, Version)]
    public class TextureTriggerPlugin : BasePlugin
    {
        public const string Version = "0.2";
        public const string GUID = "Texture_Trigger";
        internal const string DisplayName = "Texture Trigger";

        public override void Load()
        {
            Harmony.CreateAndPatchAll(typeof(Hooks));
        }

        private static class Hooks
        {
            /// <summary>
            /// Workaround for images not getting translated
            /// Fix found by @ekibun
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(typeof(Image), nameof(Image.OnEnable))]
            public static void Postfix_Image_OnEnable(Image __instance)
            {
                // texture access triggers AT hooks
                if (__instance.sprite != null)
                    _ = __instance.sprite.texture;
            }

            /// <summary>
            /// Workaround for  keyboard shortcut dialog not getting translated (possibly others too?)
            /// </summary>
            [HarmonyPostfix]
            [HarmonyPatch(typeof(SpriteChangeCtrl), nameof(SpriteChangeCtrl.Awake))]
            [HarmonyPatch(typeof(SpriteChangeCtrl), nameof(SpriteChangeCtrl.ChangeValue))]
            [HarmonyPatch(typeof(SpriteChangeCtrl), nameof(SpriteChangeCtrl.ChangeValueSimple))]
            public static void Postfix_Image_OnEnable(SpriteChangeCtrl __instance)
            {
                if (__instance.image != null && __instance.image.sprite != null)
                    _ = __instance.image.sprite.texture;
            }
        }
    }
}
