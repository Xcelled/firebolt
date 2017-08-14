using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebolt.Core
{
    public static class Extensions
	{
		public static Signature EnsureNonEmpty(this Signature sig, string defaultName = "Unknown", string defaultEmail = "Unknown")
		{
			var blankName = string.IsNullOrEmpty(sig.Name);
			var blankEmail = string.IsNullOrEmpty(sig.Email);

			if (!blankEmail && !blankName)
			{
				return sig;
			}

			return new Signature(blankName ? defaultName : sig.Name, blankEmail ? defaultEmail : sig.Email, sig.When);
		}

		public static HashSet<T> ToSet<T>(this IEnumerable<T> @enum)
		{
			var casted = @enum as HashSet<T>;
			if (casted != null)
			{
				return casted;
			}

			return new HashSet<T>(@enum);
		}

		public static IEnumerable<T> Flatten<T>(this IEnumerable<IEnumerable<T>> @enum)
		{
			return Enumerable.SelectMany(@enum, x => x);
		}

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> @enum, params T[] items)
        {
            return @enum.Concat(items.AsEnumerable());
        }

        public static IEnumerable<T> ConcatWith<T>(this T obj, IEnumerable<T> @enum)
        {
            yield return obj;
            foreach (var x in @enum)
            {
                yield return x;
            }
        }

        public static bool DictionaryEqual<TKey, TValue>(this IDictionary<TKey, TValue> dict1, IDictionary<TKey, TValue> dict2)
            where TKey : IEquatable<TKey>
            where TValue : IEquatable<TValue>
        {
            if (dict1 == dict2) return true;
            if ((dict1 == null) || (dict2 == null)) return false;
            if (dict1.Count != dict2.Count) return false;

            foreach (var kvp in dict1)
            {
                if (!dict2.TryGetValue(kvp.Key, out TValue value2))
                {
                    return false;
                }

                if (!kvp.Value.Equals(value2))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
