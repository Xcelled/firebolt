using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Firebolt.Core.Builtins
{
	public class SubdirectoryFilter : ICommitFilter
	{
		private IDictionary<string, string> relocations;

		public SubdirectoryFilter(IDictionary<string, string> relocations)
		{
			this.relocations = relocations;

			var seen = new HashSet<string>();
            foreach (var rel in this.relocations.Values)
            {
                if (!seen.Add(rel))
                {
					throw new Exception("Multiple relocations write to " + rel);
				}
            }
		}

		public bool FilterCommit(CommitMetadata commit, FilterContext context)
		{
			var newTree = new TreeMetadata();

			foreach (var rel in relocations)
			{
                if (rel.Key == null)
                {
                    // Move whole tree under a subdir
                    throw new NotImplementedException();
                }
                else
                {
                    var subtree = commit.Tree[rel.Key];

					if (subtree != null)
					{
                        if (rel.Key == null)
                        {
                            // Move tree under the root
                            throw new NotImplementedException();
                        }
                        else
                        {
                            newTree.Add(rel.Value, subtree);
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
