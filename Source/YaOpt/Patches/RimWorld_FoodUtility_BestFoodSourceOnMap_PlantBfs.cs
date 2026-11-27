using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using YaOpt.Helpers;

namespace YaOpt.Patches
{
	/// <summary>
	/// Routes the humanlike plant food search through a region-bounded nearest-first scan.
	/// </summary>
	/// <remarks>
	/// Vanilla hands the whole <c>ThingRequestGroup.FoodSource</c> list to
	/// <see cref="FoodUtility.SpawnedFoodSearchInnerScan"/>, which scores every candidate before the
	/// validator runs. For a pawn that can eat <see cref="FoodTypeFlags.Plant"/>, that list is
	/// essentially the map's grass, so a single job attempt scores thousands of plants that the
	/// validator then rejects. The scan call is redirected to
	/// <see cref="FoodPlantSearchHelper.Search"/>, which searches plants by region expansion and
	/// keeps the flat scan for non-plant food. See the helper for the exact equivalence argument.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFoodPlantSearchBfs"/>
	/// <seealso cref="FoodPlantSearchHelper"/>
	[HarmonyPatch(typeof(FoodUtility))]
	[HarmonyPatch(nameof(FoodUtility.BestFoodSourceOnMap))]
	internal static class RimWorld_FoodUtility_BestFoodSourceOnMap_PlantBfs
	{
		/// <summary>
		/// Index of the <c>allowPlant</c> parameter of <see cref="FoodUtility.BestFoodSourceOnMap"/>.
		/// </summary>
		private const short AllowPlantArgIndex = 5;

		/// <summary>
		/// Index of the <c>forceScanWholeMap</c> parameter of
		/// <see cref="FoodUtility.BestFoodSourceOnMap"/>.
		/// </summary>
		private const short ForceScanWholeMapArgIndex = 13;

		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptFoodPlantSearchBfs.Enabled == true;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = new List<CodeInstruction>(instructions);
			var target = AccessTools.Method(typeof(FoodUtility), "SpawnedFoodSearchInnerScan", new[]
			{
				typeof(Pawn), typeof(IntVec3), typeof(List<Thing>), typeof(PathEndMode),
				typeof(TraverseParms), typeof(float), typeof(Predicate<Thing>)
			});
			var replacement = AccessTools.Method(typeof(FoodPlantSearchHelper), nameof(FoodPlantSearchHelper.Search));
			if (target == null || replacement == null)
			{
				YaOptMod.Debug("Food plant search: unable to resolve SpawnedFoodSearchInnerScan or helper, patch skipped.");
				return list;
			}

			var index = -1;
			for (var i = 0; i < list.Count; i++)
			{
				if (list[i].opcode != OpCodes.Call || !Equals(list[i].operand, target))
					continue;

				// Exactly one call site is expected. A second one means a foreign transpiler reshaped
				// the method, so leave the whole body alone instead of guessing which call to patch.
				if (index != -1)
				{
					YaOptMod.Debug("Food plant search: more than one food scan call found, patch skipped.");
					return list;
				}

				index = i;
			}

			if (index < 0)
			{
				YaOptMod.Debug("Food plant search: food scan call not found, patch skipped.");
				return list;
			}

			var call = list[index];
			var allowPlant = new CodeInstruction(OpCodes.Ldarg, AllowPlantArgIndex);

			// The two trailing booleans are pushed after the original seven arguments, so labels and
			// exception blocks of the call move to the first inserted instruction.
			allowPlant.labels.AddRange(call.labels);
			allowPlant.blocks.AddRange(call.blocks);
			call.labels.Clear();
			call.blocks.Clear();
			call.opcode = OpCodes.Call;
			call.operand = replacement;

			list.Insert(index, new CodeInstruction(OpCodes.Ldarg, ForceScanWholeMapArgIndex));
			list.Insert(index, allowPlant);
			YaOptMod.Debug("Food plant search: food scan call redirected to the region-bounded plant search.");
			return list;
		}
	}
}
