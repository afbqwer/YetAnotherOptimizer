using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace YaOpt.Helpers
{
	public static class MiscHelper
	{
		public static string DeclaringTypeName(this MethodBase method)
		{
			var type = method.DeclaringType;
			return type != null ? type.FullName : string.Empty;
		}

		public static string DeclaringTypeName(this FieldInfo field)
		{
			var type = field.DeclaringType;
			return type != null ? type.FullName : string.Empty;
		}

		public static string FullName(this MethodBase method)
		{
			return $"{method.DeclaringTypeName()}:{method.Name}";
		}

		public static string FullName(this FieldInfo field)
		{
			return $"{field.DeclaringTypeName()}:{field.Name}";
		}

		public static bool ReverseRemove<T>(this List<T> list, T t)
		{
			var index = list.LastIndexOf(t);
			if (index < 0)
				return false;

			list.RemoveAt(index);
			return true;
		}
	}
}
