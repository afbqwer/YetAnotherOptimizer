using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Verse;
using YaOpt.Patches;
using static YaOpt.Helpers.ListerThingsIndexer;

namespace YaOpt.Helpers
{
	public static class ListerThingsHelper
	{
		public const int INDEX_TYPE_DEF = (int)ThingRequestGroup.ApparelSource + 1;

		/// <summary>
		/// For compatibility purposes, it records which ThingRequestGroup has index mismatch.
		/// When a removal operation is performed, the use of the indexer for this type of
		/// ThingRequestGroup will be disabled, forcing it to fall back to the vanilla path.
		/// </summary>
		private static readonly BitArray _bannedThingRequestGroup =
			new BitArray((int)ThingRequestGroup.ApparelSource + 2);

		/// <summary>
		/// Like <c>_bannedThingRequestGroup</c>, but used for <c>IHaulSource</c>.
		/// </summary>
		private static bool _banHaulSourceIndex = false;

		/// <summary>
		/// Whether the fast removal path could actually be installed on
		/// <see cref="ListerThings.Remove"/>.
		/// </summary>
		/// <remarks>
		/// The thing index only works as a pair: <see cref="Verse_ListerThings_Add"/> records
		/// where a thing landed, and <see cref="Verse_ListerThings_Remove"/> consumes that
		/// record. If another mod transpiles <c>ListerThings.Remove</c> in a way that hides the
		/// group-removal call, the consumer disappears while the producer stays alive, which
		/// leaks one record per despawned thing and reports a duplicate on every respawn.
		/// When the removal call cannot be located, this is false and every patch that depends
		/// on the index is left unapplied, falling back to vanilla behaviour as a whole.
		/// </remarks>
		public static bool FastRemoveAvailable { get; private set; }

		/// <summary>
		/// The method that took over the removal site, or an empty string when the site could not
		/// be found at all. <c>null</c> when the fast removal is available or not applicable,
		/// so the settings panel can tell the two apart.
		/// </summary>
		public static string FastRemoveConflict { get; private set; }

		/// <summary>
		/// Decides whether the fast removal can be installed, once per patch pass.
		/// </summary>
		/// <remarks>
		/// Must run before the patches are applied, because <c>Prepare</c> of every patch that
		/// depends on the index reads the result, and those results are evaluated in an
		/// unspecified order relative to the transpiler that would report the failure.
		/// </remarks>
		internal static void EvaluateFastRemoveAvailability()
		{
			FastRemoveAvailable = false;
			FastRemoveConflict = null;
			if (!YaOptGlobal.Settings.OptFastListerRemove.Enabled)
				return;

			var method = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.Remove), new[] { typeof(Thing) });
			if (method == null)
			{
				YaOptMod.Warning("Cannot find ListerThings.Remove, the fast removal has been skipped.");
				return;
			}

			List<CodeInstruction> codes;
			try
			{
				// Other mods' transpilers are already in place at this point, so this sees the
				// same instructions our transpiler would receive.
				codes = PatchProcessor.GetCurrentInstructions(method);
			}
			catch (Exception ex)
			{
				YaOptMod.Warning("Cannot read the IL of ListerThings.Remove, the fast removal has been skipped: " + ex);
				return;
			}

			if (Verse_ListerThings_Remove.TryLocateGroupRemove(codes))
			{
				FastRemoveAvailable = true;
				return;
			}

			var conflict = FindForeignSwap(codes);
			FastRemoveConflict = conflict ?? string.Empty;
			YaOptMod.Warning("Cannot locate the group removal of ListerThings.Remove" +
							 (conflict != null ? $", the call has been replaced by {conflict}" : string.Empty) +
							 ". The fast removal, the thing index and every patch built on it " +
							 "have been skipped; ListerThings falls back to vanilla behaviour.");
		}

		/// <summary>
		/// Finds a method that took the place of <c>ListerThings.RemoveSwap</c>, for diagnostics.
		/// </summary>
		private static string FindForeignSwap(List<CodeInstruction> codes)
		{
			foreach (var code in codes)
			{
				if (code.opcode != OpCodes.Call && code.opcode != OpCodes.Callvirt)
					continue;
				if (!(code.operand is MethodInfo method))
					continue;
				if (method.DeclaringType == typeof(ListerThings))
					continue;
				if (method.ReturnType != typeof(void))
					continue;
				var parameters = method.GetParameters();
				if (parameters.Length != 2)
					continue;
				if (parameters[0].ParameterType != typeof(List<Thing>) ||
					parameters[1].ParameterType != typeof(Thing))
					continue;
				return $"{method.DeclaringType?.FullName}.{method.Name}";
			}
			return null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void AddToThingList(List<Thing> list, Thing thing,
			ThingRecord record, ListerThingsUse use, int indexType)
		{
			list.Add(thing);
			if (use != ListerThingsUse.Global)
				return;
			var index = list.Count - 1;
			if (indexType == INDEX_TYPE_DEF)
			{
				record.DefIndex = index;
			}
			else
			{
				record.GroupIndex[indexType] = index;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static List<Thing>[] GetListsByGroup(ListerThings lister)
		{
			// ListerThings.listsByGroup is private in vanilla; publicizer gives us the direct field.
			return lister.listsByGroup;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void AddToHaulList(List<IHaulSource> list, IHaulSource haulSource,
			ThingRecord record, ListerThingsUse use)
		{
			list.Add(haulSource);
			if (use != ListerThingsUse.Global)
				return;
			var index = list.Count - 1;
			record.HaulIndex = index;
		}

		static void PrintErrorForBadIndex(Thing thing, int index, int indexType, List<Thing> list)
		{
			var listType = indexType == INDEX_TYPE_DEF
				? $"ByDef({thing.def})"
				: $"{ThingListGroupHelper.AllGroups[indexType]}";
			if (index < 0 || index >= list.Count)
			{
				YaOptMod.Warning($"Thing index is out of bound: {index} for thing {thing}. " +
							   $"List count: {list.Count}. " +
							   $"List type: {listType}. " +
							   "Fallback to the original path and the indexer has been disabled for this type.");
			}
			else
			{
				YaOptMod.Warning($"Thing does not match its index: {index} for thing {thing}. " +
								 $"The actual thing in {index} is {list[index]}. " +
								 $"List type: {listType}. " +
								 "Fallback to the original path and the indexer has been disabled for this type.");
			}
		}

		static void PrintErrorForBadIndex(IHaulSource haulSource, int index, List<IHaulSource> list)
		{
			if (index < 0 || index >= list.Count)
			{
				YaOptMod.Warning($"IHaulSource index is out of bound: {index} for IHaulSource {haulSource}. " +
								 $"List count: {list.Count}. " +
								 "Fallback to the original path and the indexer has been disabled for this type.");
			}
			else
			{
				YaOptMod.Warning($"IHaulSource does not match its index: {index} for IHaulSource {haulSource}. " +
								 $"The actual IHaulSource in {index} is {list[index]}. " +
								 "Fallback to the original path and the indexer has been disabled for this type.");
			}
		}

		internal static bool RemoveFromThingList(List<Thing> list, Thing thing,
			ListerThingsIndexer indexer, ThingRecord record, ListerThingsUse use, int indexType)
		{
			int index;

			if (use != ListerThingsUse.Global)
			{
				// For the ListerThings of a Region, we use a reverse search.
				// The existence time of things in the ListerThings of a Region is polarized.
				// They either exist for a long time or will be removed soon.
				// The latter are usually located at the end of the array.
				list.ReverseRemove(thing);
				return true;
			}

			if (_bannedThingRequestGroup.Get(indexType))
			{
				// If this ThingRequestGroup is banned, fall back to the vanilla path.
				list.ReverseRemove(thing);
				return true;
			}
			else if (indexType == INDEX_TYPE_DEF)
			{
				index = record.DefIndex;
				record.DefIndex = -1;
			}
			else
			{
				index = record.GroupIndex[indexType];
				record.GroupIndex[indexType] = -1;
			}
			var listCount = list.Count;

			if (index < 0 || index >= listCount || list[index] != thing)
			{
				// Workaround for Adaptive Storage Framework and LWM's Deep Storage
				// They will remove stored things from HasGUIOverlay list
				if (index < 0 && indexType == (int)ThingRequestGroup.HasGUIOverlay)
				{
					return true;
				}
				_bannedThingRequestGroup.Set(indexType, true);
				PrintErrorForBadIndex(thing, index, indexType, list);
				list.ReverseRemove(thing);
				return true;
			}
			if (index == listCount - 1)
			{
				list.RemoveAt(index);
				return true;
			}
			var swapThing = list[listCount - 1];
			var swapThingRecord = indexer.GetThingRecord(swapThing, use);
			list.RemoveAt(listCount - 1);
			list[index] = swapThing;
			if (indexType == INDEX_TYPE_DEF)
			{
				swapThingRecord.DefIndex = index;
			}
			else
			{
				swapThingRecord.GroupIndex[indexType] = index;
			}
			return true;
		}


		internal static bool RemoveFromHaulList(List<IHaulSource> list, IHaulSource haul,
			ListerThingsIndexer indexer, ThingRecord record, ListerThingsUse use)
		{
			int index;

			if (use != ListerThingsUse.Global)
			{
				index = list.LastIndexOf(haul);
				if (index >= 0)
					list.RemoveAt(index);
				return true;
			}
			if (_banHaulSourceIndex)
			{
				list.ReverseRemove(haul);
				return true;
			}

			index = record.HaulIndex;
			record.HaulIndex = -1;
			var listCount = list.Count;

			if (index < 0 || index >= listCount || list[index] != haul)
			{
				PrintErrorForBadIndex(haul, index, list);
				_banHaulSourceIndex = true;
				list.ReverseRemove(haul);
				return true;
			}
			if (index == listCount - 1)
			{
				list.RemoveAt(index);
				return true;
			}
			var swapThing = list[listCount - 1];
			var swapThingRecord = indexer.GetThingRecord((Thing)swapThing, use);
			list.RemoveAt(listCount - 1);
			list[index] = swapThing;
			swapThingRecord.HaulIndex = index;
			return true;
		}

		internal static void RebuildIndex(ListerThings lister, ThingRequestGroup group)
		{
			// Region ListerThings haven't indexer
			if (lister.use != ListerThingsUse.Global)
				return;

			// No need to rebuild the index for banned group
			if (_bannedThingRequestGroup.Get((int)group))
				return;

			var lists = GetListsByGroup(lister);
			var indexType = (int)group;
			var list = lists[indexType];
			if (list == null)
				return;

			var indexer = GetListerThingsIndex(lister);

			for (var i = 0; i < list.Count; i++)
			{
				var thing = list[i];
				var record = indexer.TryGetThingRecord(thing, lister.use);
				if (record != null)
				{
					record.GroupIndex[indexType] = i;
				}
			}
		}
	}
}