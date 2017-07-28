using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using Firebolt.Core;

namespace Firebolt.Core.Builtins
{
    public class SubdirectoryFilter : ICommitFilter
    {
        private Dictionary<string, string> relocations;
        public SubdirectoryFilter(Dictionary<string, string> relocations)
        {
            this.relocations = relocations;
        }

        public bool FilterCommit(CommitMetadata commit, FilterContext context)
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

            if (newTree.IsEmpty)
            {
                return false;
            }

            commit.Tree = newTree;
            return true;
        }
    }
}
