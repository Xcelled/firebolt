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
    public sealed class Parentage : Edge<FireboltCommit>
    {
        // Git-centric renames
		public FireboltCommit Child => Source;
		public FireboltCommit Parent => Target;
		public Parentage(FireboltCommit child, FireboltCommit parent) : base(child, parent)
        {
        }
    }

    public class GitGraph : BidirectionalGraph<FireboltCommit, Parentage>
    {
        public GitGraph(): base(false)
        {

        }

        public GitGraph(IEnumerable<Commit> commits)
        {
            // Convert commits into a DAG representation
            var edges = commits.SelectMany(c =>
            {
                var child = FireboltCommit.From(c);
                return c.Parents.Select(p => new Parentage(child, FireboltCommit.From(p)));
            });

            AddVerticesAndEdgeRange(edges);
		}

		public IEnumerable<FireboltCommit> GetParents(FireboltCommit commit) => OutEdges(commit).Select(e => e.Parent);
		public IEnumerable<FireboltCommit> GetChildren(FireboltCommit commit) => InEdges(commit).Select(e => e.Child);
	}
}
