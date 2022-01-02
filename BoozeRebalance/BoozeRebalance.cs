using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;
using System.Reflection;

// The SaveFileHelper is good boilerplate code to paste into all your mods that need to store data in the save file:
public static class SaveFileHelper
{
    // How to use: 
    // Main.myModsSaveContainer = SaveFileHelper.Load<MyModsSaveContainer>("MyModName");
    public static T Load<T>(this string modName) where T : new()
    {
        string xmlStr;
        if (GameState.modData != null && GameState.modData.TryGetValue(modName, out xmlStr)) {
            Debug.Log("Proceeding to parse save data for " + modName);
            System.Xml.Serialization.XmlSerializer xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
            using (System.IO.StringReader textReader = new System.IO.StringReader(xmlStr)) {
                return (T)xmlSerializer.Deserialize(textReader);
            }
        }
        Debug.Log("Cannot load data from save file. Using defaults for " + modName);
        return new T();
    }

    // How to use:
    // SaveFileHelper.Save(Main.myModsSaveContainer, "MyModName");
    public static void Save<T>(this T toSerialize, string modName)
    {
        System.Xml.Serialization.XmlSerializer xmlSerializer = new System.Xml.Serialization.XmlSerializer(typeof(T));
        using (System.IO.StringWriter textWriter = new System.IO.StringWriter()) {
            xmlSerializer.Serialize(textWriter, toSerialize);
            GameState.modData[modName] = textWriter.ToString();
            Debug.Log("Packed save data for " + modName);
        }
    }
}

namespace BoozeRebalance
{
    // This contains all the variables we will need to store in the save file. 
    // Default values are defined for saves that haven't seen this mod yet.
    public class BoozeSaveContainer
    {
        public float playerAlcoholLevel { get; set; } = 0f;
        //Add as many variables as you need...
    }

    static class Main
    {
        public static bool enabled;
        public static UnityModManager.ModEntry mod;
        // This is a good spot to add your own fields instead of adding fields to the vanilla game classes
        public static BoozeSaveContainer mySaveContainer = new BoozeSaveContainer();
        public static UnityEngine.PostProcessing.BloomModel.Settings noBloom = new UnityEngine.PostProcessing.BloomModel.Settings();

        //This is standard code, you can just copy it directly into your mod
        static bool Load(UnityModManager.ModEntry modEntry)
        {
            var harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            mod = modEntry;
            modEntry.OnToggle = OnToggle;
            return true;
        }

        //This is also standard code, you can just copy it
        static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            enabled = value;
            return true;
        }


        /*This is a patch for the PlayerAlcohol.Update() method. I wanted to change the 
         * way this method affects the graphics settings.
         * 
         * I first viewed it in dnSpy, then decided to use a Harmony postfix to overwrite the changes it makes to the bloom setting. 
         * 
         * It might be more logical to disable the line of the code that modifies the bloom in the first place, but it is much easier 
         * and safer to use a Postfix, rather than a Transpiler.
         */
        [HarmonyPatch(typeof(PlayerAlcohol), "Update")]
        static class DrunkGraphicsPatch
        {
            private static void Postfix(PlayerAlcohol __instance) //The `__instance` replaces the `this` keyword in the original method
            {
                if (Main.enabled) {
                    // Disable bloom
                    __instance.postProcessing.bloom.settings = Main.noBloom;
                    // Add some color saturation instead
                    UnityEngine.PostProcessing.ColorGradingModel.Settings settings2 = __instance.postProcessing.colorGrading.settings;
                    settings2.basic.saturation = 1f + PlayerNeeds.alcohol / 100f;
                    __instance.postProcessing.colorGrading.settings = settings2;
                }
            }
        }

        /*This is a patch for the PlayerNeeds.LateUpdate() method. I want alcohol to affect the sleep meter while the player is 
         * sleeping. (normally, it only has an effect while awake.)
         * 
         * Again, it is much safer and simpler to use a Postfix to redo some of the original methods work, rather than trying 
         * to inject code into the middle of the method.
         */
        [HarmonyPatch(typeof(PlayerNeeds), "LateUpdate")]
        static class DrunkSleepPatch
        {
            private static void Postfix()
            {
                if (Main.enabled && GameState.sleeping) {
                    // Here is the line I wanted to add
                    PlayerNeeds.sleep -= Time.deltaTime * Sun.sun.timescale * 15f * (PlayerNeeds.alcohol / 100f);
                    // I chose to repeat the cleanup steps from the original method. 
                    // These are small operations and doing them a 2nd time won't cause any performance impact.
                    if (PlayerNeeds.sleep > 100f) {
                        PlayerNeeds.sleep = 100f;
                    }
                    if (PlayerNeeds.sleep <= 0f) {
                        PlayerNeeds.sleepDebt += PlayerNeeds.sleep * 0.5f;
                        PlayerNeeds.sleep = 0f;
                    }
                    if (PlayerNeeds.sleepDebt < 0f) {
                        PlayerNeeds.sleepDebt = 0f;
                    }
                    if (PlayerNeeds.sleepDebt > 100f) {
                        PlayerNeeds.sleepDebt = 100f;
                    }
                }
            }
        }

        /*Here I am using the SaveModData() method that was added by Sailwind Mod Manager.
         * 
         * It is quite simple to add your mod's data to the save file if you pasted the SaveFileHelper into your script.
         */
        [HarmonyPatch(typeof(SaveLoadManager), "SaveModData")]
        static class SavePatch
        {
            private static void Postfix()
            {
                if (Main.enabled) {
                    Main.mySaveContainer.playerAlcoholLevel = PlayerNeeds.alcohol;
                    SaveFileHelper.Save(Main.mySaveContainer, "BoozeRebalance");
                }
            }
        }

        /*Here I am using the LoadModData() method that was added by Sailwind Mod Manager.
         * 
         * If this player hasn't used BoozeRebalance before, the Load method will just return the default value. 
         * Remember to define defaults in your own SaveContainer class.
         */
        [HarmonyPatch(typeof(SaveLoadManager), "LoadModData")]
        static class LoadGamePatch
        {
            private static void Postfix()
            {
                if (Main.enabled) {
                    //Load the entire BoozeSaveContainer from save file
                    Main.mySaveContainer = SaveFileHelper.Load<BoozeSaveContainer>("BoozeRebalance");
                    Main.mod.Logger.Log("The saved alcohol level was " + Main.mySaveContainer.playerAlcoholLevel);
                    //Apply the saved value to the player's alcohol level
                    PlayerNeeds.alcohol = Main.mySaveContainer.playerAlcoholLevel;
                }
            }
        }
    }
}
