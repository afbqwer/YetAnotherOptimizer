using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace YaOpt.Patches
{
	/// <summary>
	/// Draws unrotated flecks without building a rotation quaternion.
	/// </summary>
	/// <remarks>
	/// <see cref="Graphic_Fleck.DrawFleck"/> always calls <c>Quaternion.AngleAxis</c>, which costs a
	/// sin/cos pair, even when the angle is 0. That is the common case: <c>FleckMaker.GetDataStatic</c>
	/// never sets a rotation and only thrown flecks add <c>rotationRate</c>, while splash flecks pass 0
	/// every time. Substituting the identity quaternion for a zero angle produces the exact same matrix
	/// without the trigonometry. A transpiler is used so this composes with other patches on the method.
	/// </remarks>
	/// <seealso cref="YaOptSettings.OptFleckFastDraw"/>
	[HarmonyPatch(typeof(Graphic_Fleck), nameof(Graphic_Fleck.DrawFleck))]
	internal static class Verse_Graphic_Fleck_DrawFleck
	{
		static bool Prepare()
		{
			return YaOptGlobal.Settings?.OptFleckFastDraw.Enabled == true;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (var instruction in instructions)
			{
				if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
					instruction.operand is MethodInfo method &&
					method.Name == nameof(Quaternion.AngleAxis) &&
					method.DeclaringType?.FullName == typeof(Quaternion).FullName)
				{
					var call = CodeInstruction.Call(typeof(Verse_Graphic_Fleck_DrawFleck), nameof(AngleAxisFast));
					call.labels.AddRange(instruction.labels);
					call.blocks.AddRange(instruction.blocks);
					yield return call;
					continue;
				}
				yield return instruction;
			}
		}

		public static Quaternion AngleAxisFast(float angle, Vector3 axis)
		{
			return angle == 0f ? Quaternion.identity : Quaternion.AngleAxis(angle, axis);
		}
	}
}