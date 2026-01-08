using BepInEx;
using RoR2;
using MonoMod.Cil;
using MonoMod.Utils;
using Mono.Cecil.Cil;
using BepInEx.Logging;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace RoR2LessGuaranteedAccessNodes
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class RoR2LessGuaranteedAccessNodes : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "SSM24";
        public const string PluginName = "LessGuaranteedAccessNodes";
        public const string PluginVersion = "1.0.0";

        private static readonly Dictionary<string, float> defaultStageConfigs = new()
        {
            ["Scorched Acres"] = 0.2f,
            ["Rallypoint Delta"] = 0.5f,
            ["Sulfur Pools"] = 0.33f,
            ["Iron Alluvium/Aurora"] = 0.75f,
            ["Repurposed Crater"] = 0.6f,
            ["Fogbound Lagoon"] = 0.33f,
            ["Remote Village"] = 0.33f,
        };
        private static readonly HashSet<string> moddedStages = ["Fogbound Lagoon", "Remote Village"];

        private static readonly Dictionary<string, float> stageConfigs = new();

        public void Awake()
        {
            Log.Init(Logger);

            InitializeSettings();

            IL.RoR2.AccessCodesMissionController.OnStartServer += IL_ModifyAccessCodesMissionController;
        }

        // for ScriptEngine compat
        public void OnDestroy()
        {
            IL.RoR2.AccessCodesMissionController.OnStartServer -= IL_ModifyAccessCodesMissionController;
        }

        private void InitializeSettings()
        {
            foreach (KeyValuePair<string, float> defaultConfig in defaultStageConfigs)
            {
                string stageName = defaultConfig.Key;
                string category = moddedStages.Contains(stageName) ? "Modded Stages" : "Default Stages";
                ConfigEntry<float> config = Config.Bind(
                    category, stageName, defaultConfig.Value, $"Chance for spawn on {stageName}");
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

        private bool ShouldAccessNodesSpawn()
        {
            // guarantee spawn on specifically the second attempt this run if it was rejected the first time
            if (Run.instance.GetEventFlag(RejectedAccessNodesOnce) 
                && !Run.instance.GetEventFlag(SeenAccessNodesOnce))
            {
                Logger.LogInfo("Guaranteeing Access Node spawn...");
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
                _ => 0.5f,
            };

            float roll = Run.instance.stageRng.nextNormalizedFloat;
            Logger.LogDebug($"{sceneName}: rolled {roll} (needed: <{chance})");
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
