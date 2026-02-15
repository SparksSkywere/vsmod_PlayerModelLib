using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace PlayerModelLib;

internal static class TranspilerPatches
{
    [HarmonyPatchCategory("PlayerModelLibTranspiler")]
    public static class PatchEntityBehaviorContainerCommand
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(EntityBehaviorContainer), "addGearToShape",
            [
                typeof(ICoreAPI),
                typeof(Vintagestory.API.Common.Entities.Entity),
                typeof(ITextureAtlasAPI),
                typeof(Shape),
                typeof(ItemStack),
                typeof(IAttachableToEntity),
                typeof(string),
                typeof(string),
                typeof(string[]).MakeByRefType(),
                typeof(IDictionary<string, CompositeTexture>),
                typeof(Dictionary<string, StepParentElementTo>)
            ]);
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new(instructions);

            FieldInfo capiField = AccessTools.Field(
                typeof(EntityBehaviorContainer).GetNestedType("<>c__DisplayClass23_0", BindingFlags.NonPublic),
                "capi"
            );

            MethodInfo replacementMethod = AccessTools.Method(
                typeof(ShapeReplacementUtil),
                nameof(ShapeReplacementUtil.GetModelReplacement)
            );

            for (int i = 0; i < code.Count; i++)
            {
                // Match: ldloc.0 -> ldfld capi
                if (code[i].opcode == OpCodes.Ldloc_0 &&
                    i + 1 < code.Count &&
                    code[i + 1].LoadsField(capiField))
                {
                    // Inject BEFORE this point
                    List<CodeInstruction> injected = new()
                    {
                    // stack (ItemStack)
                    new CodeInstruction(OpCodes.Ldarg_S, 4),

                    // entity
                    new CodeInstruction(OpCodes.Ldarg_1),

                    // ref shape (local 3)
                    new CodeInstruction(OpCodes.Ldloca_S, (byte)3),

                    // ref compositeShape (local 5)
                    new CodeInstruction(OpCodes.Ldloca_S, (byte)5),

                    // iatta
                    new CodeInstruction(OpCodes.Ldarg_S, 5),

                    // damageEffect (local 1)
                    new CodeInstruction(OpCodes.Ldloc_1),

                    // slotCode
                    new CodeInstruction(OpCodes.Ldarg_S, 6),

                    // ref willDeleteElements (arg 8 already byref)
                    new CodeInstruction(OpCodes.Ldarg_S, 8),

                    // call
                    new CodeInstruction(OpCodes.Call, replacementMethod)
                };

                    code.InsertRange(i, injected);
                    break;
                }
            }

            return code;
        }
    }

    [HarmonyPatchCategory("PlayerModelLibTranspiler")]
    [HarmonyPatch(typeof(EntityPlayer), "updateEyeHeight")]
    public static class PatchEntityPlayerUpdateEyeHeight
    {
        public static float GetSneakEyeMultiplier(EntityPlayer player)
        {
            PlayerSkinBehavior? skinBehavior = player.GetBehavior<PlayerSkinBehavior>();

            if (skinBehavior == null) return 0.8f;

            return skinBehavior.CurrentModel.SneakEyeHeightMultiplier;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            MethodInfo getMultMethod = AccessTools.Method(typeof(PatchEntityPlayerUpdateEyeHeight), nameof(GetSneakEyeMultiplier));

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldc_R8 && (double)codes[i].operand == 0.800000011920929)
                {
                    codes[i] = new CodeInstruction(OpCodes.Ldarg_0);
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, getMultMethod));
                    i++;
                }
            }

            return codes;
        }
    }

    [HarmonyPatchCategory("PlayerModelLibTranspiler")]
    [HarmonyPatch(typeof(EntityPlayer), "updateEyeHeight")]
    public static class PatchEntityPlayerUpdateEyeHeightInsertBeforeFloorSitting
    {
        public static void ApplyEyeHightModifiers(EntityPlayer player, ref double newEyeheight, ref double newModelHeight)
        {
            float modifier = GetEyeHightModifier(player);
            newEyeheight *= modifier;
            newModelHeight *= modifier;
        }

        public static float GetEyeHightModifier(EntityPlayer player)
        {
            PlayerSkinBehavior? skinBehavior = player.GetBehavior<PlayerSkinBehavior>();

            if (skinBehavior == null) return 1f;

            bool moving = (player.Controls.TriesToMove && player.SidedPos.Motion.LengthSq() > 0.00001) && !player.Controls.NoClip && !player.Controls.DetachedMode;
            bool walking = moving && player.OnGround;

            if (walking && !player.Controls.Backward && !player.Controls.Sneak && !player.Controls.IsClimbing && !player.Controls.IsFlying)
            {
                if (player.Controls.Sprint)
                {
                    return skinBehavior.CurrentModel.SprintEyeHeightMultiplier;
                }
                else
                {
                    return skinBehavior.CurrentModel.WalkEyeHeightMultiplier;
                }
            }

            return 1;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);
            MethodInfo callMethod = AccessTools.Method(typeof(PatchEntityPlayerUpdateEyeHeightInsertBeforeFloorSitting), nameof(ApplyEyeHightModifiers));

            for (int i = 0; i < codes.Count - 2; i++)
            {
                if (codes[i].opcode == OpCodes.Ldloc_2 &&
                    codes[i + 1].opcode == OpCodes.Callvirt &&
                    codes[i + 1].operand is MethodInfo mi &&
                    mi.Name == "get_FloorSitting")
                {
                    // Insert before i
                    codes.InsertRange(i, new[]
                    {
                        new CodeInstruction(OpCodes.Ldarg_0),        // this (EntityPlayer)
                        new CodeInstruction(OpCodes.Ldloca_S, 4),    // ref newEyeheight
                        new CodeInstruction(OpCodes.Ldloca_S, 5),    // ref newModelHeight
                        new CodeInstruction(OpCodes.Call, callMethod)
                    });
                    break;
                }
            }

            return codes;
        }
    }

    [HarmonyPatchCategory("PlayerModelLibTranspiler")]
    [HarmonyPatch(typeof(Entity), "FromBytes", [typeof(BinaryReader), typeof(bool)])]
    public static class PatchEntityFromBytesRemoveMaxSaturation
    {
        static readonly MethodInfo SetFloatMethod = AccessTools.Method(typeof(ITreeAttribute), nameof(ITreeAttribute.SetFloat));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new(instructions);

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Callvirt &&
                    codes[i].operand is MethodInfo mi &&
                    mi == SetFloatMethod)
                {
                    codes.RemoveRange(i - 3, 4);
                    i -= 4;
                }
            }

            return codes;
        }
    }

    [HarmonyPatchCategory("PlayerModelLibTranspiler")]
    [HarmonyPatch(typeof(EntityBehaviorBodyTemperature), "updateWearableConditions")]
    public static class Patch_UpdateWearableConditions
    {
        private static void ApplyWarmthStats(ref float clothingBonus, EntityAgent agent)
        {
            float value = clothingBonus;
            value += Math.Clamp(agent.Stats.GetBlended(StatsPatches.WarmthBonusStat), -100, 100);
            clothingBonus = value;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new(instructions);

            FieldInfo clothingBonusField = AccessTools.Field(
                typeof(EntityBehaviorBodyTemperature),
                "clothingBonus");

            FieldInfo entityField = AccessTools.Field(
                typeof(EntityBehavior),
                "entity");

            MethodInfo hookMethod = AccessTools.Method(
                typeof(Patch_UpdateWearableConditions),
                nameof(ApplyWarmthStats));

            for (int i = code.Count - 1; i >= 0; i--)
            {
                if (code[i].opcode == OpCodes.Ret)
                {
                    List<CodeInstruction> insert =
                    [
                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldflda, clothingBonusField),

                        new CodeInstruction(OpCodes.Ldarg_0),
                        new CodeInstruction(OpCodes.Ldfld, entityField),
                        new CodeInstruction(OpCodes.Isinst, typeof(EntityAgent)),

                        new CodeInstruction(OpCodes.Call, hookMethod)
                    ];

                    code.InsertRange(i, insert);
                    break;
                }
            }

            return code;
        }
    }
}