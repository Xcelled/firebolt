using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace Firebolt.Builtins
{
    class SubdirectoryFilter : ICommitFilter
    {
        private Dictionary<string, string> relocations;
        public SubdirectoryFilter(Dictionary<string, string> relocations)
        {
            this.relocations = relocations;
        }

		public IEnumerable<FireboltCommit> FilterCommit(FireboltCommit commit, Commit original, IRepository repo)
		{
            var newTree = new TreeMetadata();

            foreach (var rel in relocations)
            {
                var subtree = commit.Tree[rel.Key];

                if (subtree != null)
                {
                    newTree.Add(rel.Value, subtree);
                }
            }

            if (newTree.EntryNames.Any()) {
			    yield return FireboltCommit.From(commit, tree: newTree);                
            }
		}
	}
}
