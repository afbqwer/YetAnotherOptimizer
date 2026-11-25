using System.Collections.Generic;

namespace YaOpt.Helpers.ThreadLocal
{
	/// <summary>
	/// Creates reusable per-thread scratch collections.
	/// </summary>
	public static class ThreadLocalHelper
	{
		public static HashSet<T> NewSet<T>() => new HashSet<T>();
	}
}
