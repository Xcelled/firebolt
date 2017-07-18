using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace Firebolt.Builtins
{
    class SubdirectoryFilter : IFilter, ICommitLimiter
    {
        private Dictionary<string, string> relocations;
        public SubdirectoryFilter(Dictionary<string, string> relocations)
        {
            this.relocations = relocations;
        }

        public IEnumerable<Commit> Limit(IEnumerable<Commit> current)
        {
            return current.Where(keepCommit);
        }

        private bool keepCommit(Commit commit)
        {
            var parentsCount = commit.Parents.Count();
            if (parentsCount > 1)
            {
                return true; // Merge commit
            }

            var q = relocations.Keys.Where(p => commit.Tree[p] != null);
            if (parentsCount == 1)
            {
                var parentTree = commit.Parents.First().Tree;
                q = q.Where(p => commit.Tree[p] != parentTree[p]);
            }

            return q.Any();
        }

        public IEnumerable<CommitMetadata> Run(CommitMetadata commit, Commit original, IRepository repo)
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

            return new CommitMetadata[] { CommitMetadata.From(commit, tree: newTree) };
        }
    }
}
