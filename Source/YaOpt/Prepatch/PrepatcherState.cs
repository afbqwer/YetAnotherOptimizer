using System;
using System.Reflection;
using System.Text;
using Verse;
using Waylith.Injection;
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
				HasInjectedAccessorField(nameof(PrepatcherFields.Lister), typeof(ListerBuildings)) &&
				HasInjectedField(typeof(ListerBuildings), typeof(ListerBuildingsCache.State)) &&
				settings.OptListerBuildingsCache.Enabled;
			ImmunityActive = ImmunityPatched &&
				HasInjectedAccessorField(nameof(PrepatcherFields.Immunity), typeof(ImmunityHandler)) &&
				HasInjectedField(typeof(ImmunityHandler), typeof(ImmunityCache.State)) &&
				settings.OptImmunityCache.Enabled;
			RefuelCandidatesActive = RefuelPatched &&
				HasInjectedAccessorField(nameof(PrepatcherFields.RefuelCandidates), typeof(Map)) &&
				HasInjectedField(typeof(Map), typeof(RefuelWorkCandidates.State)) &&
				settings.OptRefuelSearchCache.Enabled;

			// A field-name match is the reliable gate here: Pawn already has other long fields,
			// so a type match would false-positive, and the rewritten accessor's IL body is not
			// observable through GetMethodBody (Waylith rewrites at the JIT layer).
			InvisibilityActive = settings.OptInvisibilityCache.Enabled &&
				HasInjectedAccessorField(nameof(PrepatcherFields.Invisibility), typeof(Pawn)) &&
				PawnInvisibilityCache.Resolve();
			if (settings.OptInvisibilityCache.Enabled && !InvisibilityActive)
				WarnInvisibilityInactive();
		}

		/// <summary>
		/// Explains why the invisibility gate stayed closed. The injected field is the reliable
		/// signal; the accessor body is reported only as evidence that <c>GetMethodBody</c> cannot
		/// observe a JIT-layer rewrite.
		/// </summary>
		private static void WarnInvisibilityInactive()
		{
			var field = HasInjectedAccessorField(nameof(PrepatcherFields.Invisibility), typeof(Pawn));
			var sb = new StringBuilder("Invisibility cache is enabled but stayed inactive: ");
			if (field)
				sb.Append("vanilla members could not be resolved. ");
			else
				sb.Append("the per-pawn field was not injected (field injection unavailable?). ");

			sb.Append("[diag] ")
				.Append("fieldInjection=").Append(FieldInjectionSupported()).Append(", ")
				.Append("injectedField=").Append(field).Append(", ")
				.Append("accessorBody=").Append(HasAccessorBody(nameof(PrepatcherFields.Invisibility)))
				.Append(", ")
				.Append("resolve=").Append(DescribeResolve()).Append(", ")
				.Append("fieldPeers=").Append(DescribeFieldPeers()).Append(", ")
				.Append("bodyPeers=").Append(DescribeBodyPeers());
			YaOptMod.Warning(sb.ToString());
		}

		/// <summary>
		/// Reports the runtime field-injection capability the built-in Prepatcher depends on.
		/// </summary>
		private static string FieldInjectionSupported()
		{
			return Injection.IsSupported.ToString();
		}

		/// <summary>
		/// Reports which vanilla members <see cref="PawnInvisibilityCache.Resolve"/> managed to
		/// bind. Safe to call here: Resolve caches its attempt, so a repeat only reads state.
		/// </summary>
		private static string DescribeResolve()
		{
			try
			{
				var resolved = PawnInvisibilityCache.Resolve();
				var curve = PawnInvisibilityCache.RevealCurveAvailable;
				return "resolved=" + resolved + ", revealCurve=" + curve;
			}
			catch (Exception ex)
			{
				return "probe threw " + ex.GetType().Name;
			}
		}

		/// <summary>
		/// Reliable peer state from the injected fields. If all four are present the gate logic
		/// is sound and the local failure is specific to invisibility's vanilla members.
		/// </summary>
		private static string DescribeFieldPeers()
		{
			return "lister=" + HasInjectedAccessorField(nameof(PrepatcherFields.Lister), typeof(ListerBuildings)) +
				", immunity=" + HasInjectedAccessorField(nameof(PrepatcherFields.Immunity), typeof(ImmunityHandler)) +
				", refuel=" + HasInjectedAccessorField(nameof(PrepatcherFields.RefuelCandidates), typeof(Map));
		}

		/// <summary>
		/// Unreliable peer state, kept only to show that GetMethodBody cannot see JIT rewrites.
		/// </summary>
		private static string DescribeBodyPeers()
		{
			return "lister=" + HasAccessorBody(nameof(PrepatcherFields.Lister)) +
				", immunity=" + HasAccessorBody(nameof(PrepatcherFields.Immunity)) +
				", refuel=" + HasAccessorBody(nameof(PrepatcherFields.RefuelCandidates));
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

		/// <summary>
		/// Detects a successful injection by the field it added to the target type. This is
		/// reliable: WaylithClr really inserts the field into the runtime type, so reflection
		/// sees it. Unlike that, the accessor's rewritten IL body lives in the JIT layer and is
		/// not visible through <c>GetMethodBody</c>.
		/// </summary>
		/// <remarks>
		/// The field name is generated by Waylith.Injection as
		/// <c>assemblyName + accessorName + (MetadataToken &amp; 0xFFFFFF)</c>. Reproducing that
		/// formula on the same accessor gives an exact name, so there is no type-collision risk
		/// (which is why the invisibility gate cannot use a long-field type match).
		/// </remarks>
		private static bool HasInjectedAccessorField(string accessorName, Type target)
		{
			try
			{
				var accessor = typeof(PrepatcherFields).GetMethod(accessorName,
					BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (accessor == null)
					return false;

				var expected = accessor.Module.Assembly.GetName().Name + accessor.Name +
					(accessor.MetadataToken & 0xFFFFFF);
				var fields = target.GetFields(BindingFlags.Instance | BindingFlags.Public |
											 BindingFlags.NonPublic);
				for (var i = 0; i < fields.Length; i++)
				{
					if (fields[i].Name == expected)
						return true;
				}
			}
			catch
			{
				// Injection may still be pending; treat as not injected.
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