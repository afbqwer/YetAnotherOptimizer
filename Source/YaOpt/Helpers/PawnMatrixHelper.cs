using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using NVec3 = System.Numerics.Vector3;
using NVec4 = System.Numerics.Vector4;

namespace YaOpt.Helpers
{
	/// <summary>
	/// Fused replacement for <c>PawnRenderTree.ComputeMatrix</c>.
	/// </summary>
	/// <remarks>
	/// The vanilla method multiplies the running matrix by up to five matrices that it builds on the
	/// spot, and inverts a pure translation through a full 4x4 inversion. The whole chain is a single
	/// affine transform <c>T(offset + pivot) * R * S * T(-pivot)</c>, i.e.
	/// <c>p -&gt; R*S*(p - pivot) + pivot + offset</c>, so one fused multiply is enough. When there is
	/// no rotation the transform is diagonal and stays scalar; the rotation path feeds the four rows of
	/// the running matrix through <see cref="NVec4"/> so the row combination runs on SIMD lanes.
	/// </remarks>
	internal static class PawnMatrixHelper
	{
		/// <summary>
		/// The <paramref name="tree"/> argument mirrors the receiver of the original instance call so
		/// the transpiler can swap the call target without rewiring the evaluation stack.
		/// </summary>
		internal static void ComputeMatrix(PawnRenderTree tree, ref Matrix4x4 matrix,
			in Vector3 offset, in Vector3 pivot, in Quaternion rotation, in Vector3 scale, bool canRotate)
		{
			var hasOffset = offset.x != 0f || offset.y != 0f || offset.z != 0f;
			var hasPivot = pivot.x != 0f || pivot.y != 0f || pivot.z != 0f;
			var hasScale = scale.x != 1f || scale.y != 1f || scale.z != 1f;
			var hasRotation = canRotate &&
				(rotation.x != 0f || rotation.y != 0f || rotation.z != 0f || rotation.w != 1f);

			if (!hasRotation)
			{
				ApplyScaleOnly(ref matrix, in offset, in pivot, in scale, hasScale, hasOffset, hasPivot);
				return;
			}

			if (!TryRotationRows(in rotation, out var row0, out var row1, out var row2))
			{
				// Vanilla's Rotate returns identity for a degenerate quaternion.
				ApplyScaleOnly(ref matrix, in offset, in pivot, in scale, hasScale, hasOffset, hasPivot);
				return;
			}

			if (hasScale)
			{
				var s = new NVec3(scale.x, scale.y, scale.z);
				row0 *= s;
				row1 *= s;
				row2 *= s;
			}

			var p = new NVec3(pivot.x, pivot.y, pivot.z);
			var tx = offset.x + pivot.x - NVec3.Dot(row0, p);
			var ty = offset.y + pivot.y - NVec3.Dot(row1, p);
			var tz = offset.z + pivot.z - NVec3.Dot(row2, p);

			var v0 = new NVec4(row0, tx);
			var v1 = new NVec4(row1, ty);
			var v2 = new NVec4(row2, tz);

			ApplyRow(ref matrix.m00, ref matrix.m01, ref matrix.m02, ref matrix.m03, in v0, in v1, in v2);
			ApplyRow(ref matrix.m10, ref matrix.m11, ref matrix.m12, ref matrix.m13, in v0, in v1, in v2);
			ApplyRow(ref matrix.m20, ref matrix.m21, ref matrix.m22, ref matrix.m23, in v0, in v1, in v2);
			ApplyRow(ref matrix.m30, ref matrix.m31, ref matrix.m32, ref matrix.m33, in v0, in v1, in v2);
		}

		/// <summary>
		/// No rotation: <c>R*S</c> is the diagonal scale, so only the scale and translation change.
		/// With scale one the pivot translate and its inverse cancel exactly, leaving the offset.
		/// </summary>
		private static void ApplyScaleOnly(ref Matrix4x4 matrix, in Vector3 offset, in Vector3 pivot,
			in Vector3 scale, bool hasScale, bool hasOffset, bool hasPivot)
		{
			if (!hasScale)
			{
				if (hasOffset)
					ApplyTranslation(ref matrix, offset.x, offset.y, offset.z);
				return;
			}

			var sx = scale.x;
			var sy = scale.y;
			var sz = scale.z;
			var tx = offset.x;
			var ty = offset.y;
			var tz = offset.z;
			if (hasPivot)
			{
				tx += pivot.x - sx * pivot.x;
				ty += pivot.y - sy * pivot.y;
				tz += pivot.z - sz * pivot.z;
			}
			ApplyScaleTranslation(ref matrix, sx, sy, sz, tx, ty, tz);
		}

		private static void ApplyTranslation(ref Matrix4x4 m, float tx, float ty, float tz)
		{
			m.m03 = m.m00 * tx + m.m01 * ty + m.m02 * tz + m.m03;
			m.m13 = m.m10 * tx + m.m11 * ty + m.m12 * tz + m.m13;
			m.m23 = m.m20 * tx + m.m21 * ty + m.m22 * tz + m.m23;
			m.m33 = m.m30 * tx + m.m31 * ty + m.m32 * tz + m.m33;
		}

		private static void ApplyScaleTranslation(ref Matrix4x4 m, float sx, float sy, float sz,
			float tx, float ty, float tz)
		{
			ScaleTranslateRow(ref m.m00, ref m.m01, ref m.m02, ref m.m03, sx, sy, sz, tx, ty, tz);
			ScaleTranslateRow(ref m.m10, ref m.m11, ref m.m12, ref m.m13, sx, sy, sz, tx, ty, tz);
			ScaleTranslateRow(ref m.m20, ref m.m21, ref m.m22, ref m.m23, sx, sy, sz, tx, ty, tz);
			ScaleTranslateRow(ref m.m30, ref m.m31, ref m.m32, ref m.m33, sx, sy, sz, tx, ty, tz);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void ScaleTranslateRow(ref float x, ref float y, ref float z, ref float w,
			float sx, float sy, float sz, float tx, float ty, float tz)
		{
			var a = x;
			var b = y;
			var c = z;
			var d = w;
			x = a * sx;
			y = b * sy;
			z = c * sz;
			w = a * tx + b * ty + c * tz + d;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void ApplyRow(ref float x, ref float y, ref float z, ref float w,
			in NVec4 row0, in NVec4 row1, in NVec4 row2)
		{
			var m = new NVec4(x, y, z, w);
			var result = row0 * m.X + row1 * m.Y + row2 * m.Z;
			result.W += m.W;
			x = result.X;
			y = result.Y;
			z = result.Z;
			w = result.W;
		}

		/// <summary>
		/// Builds the rows of the column-vector rotation matrix that vanilla's
		/// <c>Matrix4x4.Rotate</c> produces. Returns false for the epsilon case where vanilla falls
		/// back to identity.
		/// </summary>
		private static bool TryRotationRows(in Quaternion q, out NVec3 row0, out NVec3 row1, out NVec3 row2)
		{
			var x = q.x;
			var y = q.y;
			var z = q.z;
			var w = q.w;
			var dot = x * x + y * y + z * z + w * w;
			if (dot <= 1e-12f)
			{
				row0 = default;
				row1 = default;
				row2 = default;
				return false;
			}

			var inv = 1f / MathF.Sqrt(dot);
			x *= inv;
			y *= inv;
			z *= inv;
			w *= inv;

			var xx = x * x;
			var yy = y * y;
			var zz = z * z;
			var xy = x * y;
			var xz = x * z;
			var yz = y * z;
			var wx = w * x;
			var wy = w * y;
			var wz = w * z;

			row0 = new NVec3(1f - 2f * (yy + zz), 2f * (xy - wz), 2f * (xz + wy));
			row1 = new NVec3(2f * (xy + wz), 1f - 2f * (xx + zz), 2f * (yz - wx));
			row2 = new NVec3(2f * (xz - wy), 2f * (yz + wx), 1f - 2f * (xx + yy));
			return true;
		}
	}
}