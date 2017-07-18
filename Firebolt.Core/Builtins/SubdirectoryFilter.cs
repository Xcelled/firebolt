using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Firebolt.Core.Builtins
{
	public class Relocation
	{
		public string From { get; }
		public string To { get; }

		public Relocation(string from, string to)
		{
			this.From = from;

			// Special case of empty = move to same place
			this.To = to == string.Empty ? from : to;
		}
	}
	public class SubdirectoryFilter : ICommitFilter
	{
		private List<Relocation> relocations;

		public SubdirectoryFilter(IEnumerable<Relocation> relocations)
		{
			this.relocations = relocations.ToList();

			var seen = new HashSet<string>();
            foreach (var rel in this.relocations)
            {
                if (!seen.Add(rel.To))
                {
					throw new Exception("Multiple relocations write to " + rel.To);
				}
            }
		}

		public bool FilterCommit(CommitMetadata commit, FilterContext context)
		{
			var newTree = new TreeMetadata();

			foreach (var rel in relocations)
			{
				if (string.IsNullOrEmpty(rel.From))
				{
					// Move whole tree under a subdir
					throw new NotImplementedException();
				}
				else
				{
					var subtree = commit.Tree[rel.From];

					if (subtree != null)
					{
						if (rel.To == null)
						{
							// Move tree under the root
							throw new NotImplementedException();
						}
						else
						{
							newTree.Add(rel.To, subtree);
						}
					}
				}
			}

			if (newTree.IsEmpty)
			{
				return false;
			}

			commit.Tree = newTree;
			return true;
		}
	}
}
