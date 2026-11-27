using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Verse;

namespace YaOpt.Helpers
{
	public static class TypeSearcher
	{
		private static readonly Dictionary<Type, Action<Type>> searchingTypeAndCallbacks = new Dictionary<Type, Action<Type>>();

		public static void Init()
		{
			LongEventHandler.QueueLongEvent(Process, "YaOpt.Loading.SearchingTypes".Translate(), false, ex =>
			{
				YaOptMod.Error($"Error while searching types: {ex}");
			});
		}

		public static void Process()
		{
			var watch = new Stopwatch();
			watch.Start();
			var types = searchingTypeAndCallbacks.Keys.ToArray();
			var count = types.Length;
			if (count == 0)
				return;

			void ScanTypes(IEnumerable<Type> candidates)
			{
				foreach (var type in candidates)
				{
					if (type == null)
						continue;

					for (var i = 0; i < count; i++)
					{
						var searchingType = types[i];
						if (searchingType.IsAssignableFrom(type))
						{
							YaOptMod.Debug($"Found a derived class of {searchingType.Name}: {type.FullName}");
							searchingTypeAndCallbacks[searchingType].Invoke(type);
						}
					}
				}
			}

			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				try
				{
					ScanTypes(assembly.GetTypes());
				}
				catch (ReflectionTypeLoadException ex)
				{
					YaOptMod.Warning($"Partial type load failure in assembly '{assembly.FullName}': " +
						string.Join(Environment.NewLine, ex.LoaderExceptions.Select(error => error?.Message)));
					ScanTypes(ex.Types);
				}
				catch (Exception ex)
				{
					YaOptMod.Error(ex.ToString());
				}
			}
			watch.Stop();
			YaOptMod.Debug($"Searching types time cost: {watch.Elapsed.TotalMilliseconds}ms");
		}

		public static void RegisterSearchingType(Type type, Action<Type> callback)
		{
			searchingTypeAndCallbacks[type] = callback;
		}
	}
}