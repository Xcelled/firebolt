using LibGit2Sharp;
using System.Collections.Generic;
using System.Linq;

namespace Firebolt.Core
{
    static class Extensions
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
	}
}
