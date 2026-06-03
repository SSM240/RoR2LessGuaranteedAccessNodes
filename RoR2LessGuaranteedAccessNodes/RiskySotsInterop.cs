using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RiskySotS.Tweaks.Progression;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace RoR2LessGuaranteedAccessNodes
{
    internal static class RiskySotsInterop
    {
        private static ILHook riskySotsHook;

        public static void Load()
        {
            MethodInfo riskySotsHookMethod = typeof(HabitatAccessNodes)
                .GetMethod("SceneDirector_onPrePopulateSceneServer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (riskySotsHookMethod != null)
            {
                riskySotsHook = new ILHook(riskySotsHookMethod, IL_ModifyRiskySotsBehavior);
            }
            else
            {
                Log.Error("Could not find SceneDirector_onPrePopulateSceneServer in RiskySotS code");
            }
        }

        public static void Unload()
        {
            riskySotsHook?.Dispose();
            riskySotsHook = null;
        }

        private static void IL_ModifyRiskySotsBehavior(ILContext il)
        {
            ILCursor cursor = new(il);
            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallOrCallvirt(typeof(HabitatAccessNodes), "IsHabitat")))
            {
                cursor.EmitDelegate(RoR2LessGuaranteedAccessNodes.ShouldAccessNodesSpawn);
                cursor.Emit(OpCodes.And);
            }
            else
            {
                Log.Error("Failed to match IsHabitat check in RiskySotS code");
            }
        }
    }
}
