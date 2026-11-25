using System.Reflection;
using Verse;
using YaOpt.Helpers;

namespace YaOpt.Prepatch
{
	/// <summary>
	/// Runtime gates for IL rewritten by the built-in Prepatcher.
	/// </summary>
	/// <remarks>
	/// Free patches run before mod settings are loaded, so the rewritten methods always check
	/// these flags. Refresh runs after settings are available and only enables a path when both
	/// its free patch and its injected field accessors were installed successfully.
	/// </remarks>
	internal static class PrepatcherState
	{
		internal static bool ListerPatched;
		internal static bool ImmunityPatched;
		internal static bool RefuelPatched;

		internal static bool ListerBuildingsActive;
		internal static bool ImmunityActive;
		internal static bool RefuelCandidatesActive;

		/// <summary>
		/// True when the packed invisibility field is injected and the vanilla members the cache
		/// depends on were resolved. Every invisibility patch checks this in <c>Prepare</c>, because
		/// calling an uninjected <c>extern</c> accessor would throw at runtime.
		/// </summary>
		internal static bool InvisibilityActive;

		internal static void MarkListerPatched()
		{
			ListerPatched = true;
		}

		internal static void MarkImmunityPatched()
		{
			ImmunityPatched = true;
		}

		internal static void MarkRefuelPatched()
		{
			RefuelPatched = true;
		}

		internal static void Refresh()
		{
			var settings = YaOptMod.Instance?.Settings;
			if (settings == null)
			{
				ListerBuildingsActive = false;
				ImmunityActive = false;
				RefuelCandidatesActive = false;
				InvisibilityActive = false;
				return;
			}

			ListerBuildingsActive = ListerPatched &&
				HasInjectedField(typeof(ListerBuildings), typeof(ListerBuildingsCache.State)) &&
				HasAccessorBody(nameof(PrepatcherFields.Lister)) &&
				settings.OptListerBuildingsCache.Enabled;
			ImmunityActive = ImmunityPatched &&
				HasInjectedField(typeof(ImmunityHandler), typeof(ImmunityCache.State)) &&
				HasAccessorBody(nameof(PrepatcherFields.Immunity)) &&
				settings.OptImmunityCache.Enabled;
			RefuelCandidatesActive = RefuelPatched &&
				HasInjectedField(typeof(Map), typeof(RefuelWorkCandidates.State)) &&
				HasAccessorBody(nameof(PrepatcherFields.RefuelCandidates)) &&
				settings.OptRefuelSearchCache.Enabled;

			// The accessor check is the only reliable one here: Pawn already has other long
			// fields, so a type match would false-positive.
			InvisibilityActive = settings.OptInvisibilityCache.Enabled &&
				HasAccessorBody(nameof(PrepatcherFields.Invisibility)) &&
				PawnInvisibilityCache.Resolve();
			if (settings.OptInvisibilityCache.Enabled && !InvisibilityActive)
			{
				YaOptMod.Warning("Invisibility cache is enabled but stayed inactive: " +
					(HasAccessorBody(nameof(PrepatcherFields.Invisibility))
						? "vanilla members could not be resolved."
						: "the per-pawn field was not injected (field injection unavailable?)."));
			}
		}

		private static bool HasInjectedField(System.Type target, System.Type fieldType)
		{
			try
			{
				var fields = target.GetFields(BindingFlags.Instance | BindingFlags.Public |
											 BindingFlags.NonPublic);
				for (var i = 0; i < fields.Length; i++)
				{
					if (fields[i].FieldType == fieldType ||
						fields[i].FieldType.FullName == fieldType.FullName)
						return true;
				}
			}
			catch
			{
				// Fall through to the accessor check.
			}
			return false;
		}

		private static bool HasAccessorBody(string name)
		{
			try
			{
				var method = typeof(PrepatcherFields).GetMethod(name,
					BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				var body = method?.GetMethodBody();
				return body != null && body.GetILAsByteArray()?.Length > 0;
			}
			catch
			{
				return false;
			}
		}
	}
}