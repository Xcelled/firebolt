using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;
using QuickGraph.Algorithms;
using LibGit2Sharp;

namespace Firebolt.Core
{
    public sealed class Parentage<TCommit> : Edge<TCommit>
    {
        // Git-centric renames
		public TCommit Child => Source;
		public TCommit Parent => Target;
		public Parentage(TCommit child, TCommit parent) : base(child, parent)
        {
        }
    }

    public class GitGraph<TCommit> : BidirectionalGraph<TCommit, Parentage<TCommit>>
    {
        public GitGraph(): base(false)
        {
        }

		public IEnumerable<TCommit> GetParents(TCommit commit) => OutEdges(commit).Select(e => e.Parent);
		public IEnumerable<TCommit> GetChildren(TCommit commit) => InEdges(commit).Select(e => e.Child);
        public bool IsRootCommit(TCommit commit) => GetParents(commit).Any() == false;

        /// <summary>
        /// Removes a commit, hooking its parents and children together
        /// </summary>
        /// <param name="commit"></param>
        public void RemoveCommit(TCommit commit)
        {
            var parents = GetParents(commit);
            var children = GetChildren(commit);
            var newEdges = children.SelectMany(c => parents.Select(p => new Parentage<TCommit>(c, p)));

            AddEdgeRange(newEdges);
            RemoveVertex(commit);
        }
    }
}
