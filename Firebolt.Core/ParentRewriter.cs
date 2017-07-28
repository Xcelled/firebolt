using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt.Core
{
    public interface IParentFilter
    {
        Task<bool> FilterParents(CommitMetadata commit, ParentFilterContext context);
    }

    public class ParentFilterContext : FilterContext
    {
        private Dictionary<CommitMetadata, Task<CommitMetadata>> parentRewriters;
        private GitGraph<CommitMetadata> graph;

        public ParentFilterContext(IRepository repo, Dictionary<string, CommitMetadata> commitMap, Dictionary<CommitMetadata, Task<CommitMetadata>> rewriters, GitGraph<CommitMetadata> graph)
            : base(repo, commitMap)
        {
            this.parentRewriters = rewriters;
            this.graph = graph;
        }

        public bool AddRelationship(CommitMetadata parent, CommitMetadata child) => graph.AddEdge(new Parentage<CommitMetadata>(child, parent));
        public bool BreakRelationship(CommitMetadata parent, CommitMetadata child) => graph.RemoveEdge(new Parentage<CommitMetadata>(child, parent));
        public IEnumerable<CommitMetadata> GetParents(CommitMetadata commit) => graph.GetParents(commit);
        public Task<CommitMetadata[]> WaitForRewritten(IEnumerable<CommitMetadata> commits) => Task.WhenAll(commits.Select(p => parentRewriters[p]));
        public Task<CommitMetadata[]> WaitForRewritten(params CommitMetadata[] commits) => WaitForRewritten(commits);

        public async Task<CommitMetadata[]> WaitForRewrittenParents(CommitMetadata commit)
        {
            while(true)
            {
                var parents = await WaitForRewritten(graph.GetParents(commit));
                if (!parents.Contains(null))
                {
                    return parents;
                }
                // Else one was remove, so get them again
            }
        }
    }

    // Rewrites commit metadata in isolation with no regard to parents or children. Highly parallelizable
    class ParentRewriter
    {
        List<IParentFilter> filters;
        ParentFilterContext context;
        private GitGraph<CommitMetadata> graph;
        private Dictionary<string, CommitMetadata> map;
        private IRepository repo;

        public ParentRewriter(IRepository repo, IEnumerable<IParentFilter> filters, Dictionary<string, CommitMetadata> commitMap, GitGraph<CommitMetadata> graph)
        {
            this.repo = repo;
            this.map = commitMap;
            this.filters = filters.ToList();
            this.graph = graph;
        }

        public void Run()
        {
            var rewriterTasks = graph.Vertices
                .AsParallel()
                .ToDictionary(c => c, c => new Task<Task<CommitMetadata>>(() => filterCommit(c)));

            var rewriters = rewriterTasks
                .AsParallel()
                .ToDictionary(e => e.Key, e => e.Value.Unwrap());

            context = new ParentFilterContext(repo, map, rewriters, graph);
            Parallel.ForEach(rewriterTasks.Values, t => t.Start());
            Task.WaitAll(rewriters.Values.ToArray());
        }

        private async Task<CommitMetadata> filterCommit(CommitMetadata commit)
        {
            if (commit.IsBoundary)
            {
                // Can't change these
                return commit;
            }

            foreach (var f in filters)
            {
                // If filter returns false, drop this commit
                if (await f.FilterParents(commit, context) == false)
                {
                    graph.RemoveCommit(commit);
                    return null;
                }
            }
            return commit;
        }
    }
}
