using BepInEx;
using RoR2;
using MonoMod.Cil;
using MonoMod.Utils;
using Mono.Cecil.Cil;
using BepInEx.Logging;
using System.Collections.Generic;
using BepInEx.Configuration;
using System.Reflection;
using System;
using MonoMod.RuntimeDetour;

namespace RoR2LessGuaranteedAccessNodes
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.RiskyLives.RiskySotS", BepInDependency.DependencyFlags.SoftDependency)]
    public class RoR2LessGuaranteedAccessNodes : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "SSM24";
        public const string PluginName = "LessGuaranteedAccessNodes";
        public const string PluginVersion = "1.0.0";

        private static ConfigEntry<bool> guaranteedOnSecondRoll;
        private static ConfigEntry<bool> guaranteedAfterLoop;

        private static readonly Dictionary<string, float> defaultStageConfigs = new()
        {
            ["Scorched Acres"] = 0.2f,
            ["Rallypoint Delta"] = 0.5f,
            ["Sulfur Pools"] = 0.33f,
            ["Iron Alluvium/Aurora"] = 0.75f,
            ["Repurposed Crater"] = 0.6f,
            ["Treeborn Colony / Golden Dieback"] = 0.33f,
            ["Fogbound Lagoon"] = 0.33f,
            ["Remote Village"] = 0.33f,
        };
        private static readonly HashSet<string> moddedStages = ["Fogbound Lagoon", "Remote Village"];

        private static readonly Dictionary<string, float> stageConfigs = new();

        private bool riskySotsLoaded = false;

        public void Awake()
        {
            Log.Init(Logger);

            InitializeSettings();

            IL.RoR2.AccessCodesMissionController.OnStartServer += IL_ModifyAccessCodesMissionController;

            riskySotsLoaded = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.RiskyLives.RiskySotS");
            if (riskySotsLoaded)
            {
                RiskySotsInterop.Load();
            }
        }

        // for ScriptEngine compat
        public void OnDestroy()
        {
            IL.RoR2.AccessCodesMissionController.OnStartServer -= IL_ModifyAccessCodesMissionController;

            if (riskySotsLoaded)
            {
                RiskySotsInterop.Unload();
            }
        }

        private void InitializeSettings()
        {
            guaranteedOnSecondRoll = Config.Bind("Basic Settings", "Guaranteed by Second Roll", true,
                """
                If the first Access Node roll fails, guarantee the spawn on the second roll.
                Will not do anything if the first roll succeeds.
                """);
            guaranteedAfterLoop = Config.Bind("Basic Settings", "Guaranteed After Looping", false,
                "Guarantees Access Node spawn after looping at least once.");

            foreach (KeyValuePair<string, float> defaultConfig in defaultStageConfigs)
            {
                string stageName = defaultConfig.Key;
                string category = moddedStages.Contains(stageName) ? "Modded Stages" : "Default Stages";
                string desc = $"Chance for spawn on {stageName}";
                if (stageName == "Treeborn Colony / Golden Dieback")
                {
                    desc += "\nDoes nothing unless a mod like RiskySotS adds Access Node spawns to this stage";
                }
                ConfigEntry<float> config = Config.Bind(category, stageName, defaultConfig.Value, desc);
                stageConfigs[stageName] = config.Value;
            }
        }

        private void IL_ModifyAccessCodesMissionController(ILContext il)
        {
            ILCursor cursor = new(il);
            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchStloc(0)))
            {
                // setting loc.0 to false disables the access nodes
                cursor.Emit(OpCodes.Ldloc_0);
                cursor.EmitDelegate(ShouldAccessNodesSpawn);
                cursor.Emit(OpCodes.And);
                cursor.Emit(OpCodes.Stloc_0);
            }
        }

        private const string RejectedAccessNodesOnce = "ssm_RejectedAccessNodes";
        private const string SeenAccessNodesOnce = "ssm_SeenAccessNodes";

        internal static bool ShouldAccessNodesSpawn()
        {
            // guarantee spawn post-loop if setting is turned on
            if (guaranteedAfterLoop.Value && Run.instance.loopClearCount > 0)
            {
                Log.Info("Guaranteeing post-loop Access Node spawn...");
                return true;
            }

            // guarantee spawn on the second attempt this run if it was rejected the first time
            if (guaranteedOnSecondRoll.Value
                && Run.instance.GetEventFlag(RejectedAccessNodesOnce) 
                && !Run.instance.GetEventFlag(SeenAccessNodesOnce))
            {
                Log.Info("Guaranteeing second-attempt Access Node spawn...");
                Run.instance.SetEventFlag(SeenAccessNodesOnce);
                return true;
            }

            string sceneName = SceneCatalog.GetSceneDefForCurrentScene().baseSceneName;
            float chance = sceneName switch
            {
                "wispgraveyard" => stageConfigs["Scorched Acres"],
                "frozenwall" => stageConfigs["Rallypoint Delta"],
                "sulfurpools" => stageConfigs["Sulfur Pools"],
                "ironalluvium" => stageConfigs["Iron Alluvium/Aurora"],
                "ironalluvium2" => stageConfigs["Iron Alluvium/Aurora"],
                "FBLScene" => stageConfigs["Fogbound Lagoon"],
                "agatevillage" => stageConfigs["Remote Village"],
                "repurposedcrater" => stageConfigs["Repurposed Crater"],
                "habitat" => stageConfigs["Treeborn Colony / Golden Dieback"],
                "habitatfall" => stageConfigs["Treeborn Colony / Golden Dieback"],
                _ => 0.5f,
            };

            float roll = Run.instance.stageRng.nextNormalizedFloat;
            Log.Debug($"{sceneName}: rolled {roll} (needed: <{chance})");
            if (roll < chance)
            {
                Run.instance.SetEventFlag(SeenAccessNodesOnce);
                return true;
            }
            else
            {
                Run.instance.SetEventFlag(RejectedAccessNodesOnce);
                return false;
            }
        }
    }
}
