using HarmonyLib;
using System;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>Reads the immutable sealed flag directly instead of locking the vanilla cache.</summary>
	[HarmonyPatch(typeof(GenTypes), nameof(GenTypes.IsSealedWithCache))]
	internal static class Verse_GenTypes_IsSealedWithCache
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings.OptGenTypesLockFree.Enabled;
		}

		static bool Prefix(Type type, ref bool __result)
		{
			if (type == null)
				return true;

			__result = type.IsSealed;
			return false;
		}
	}
}