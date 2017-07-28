using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;
using QuickGraph.Algorithms;
using QuickGraph.Algorithms.Search;
using QuickGraph.Algorithms.Observers;

namespace Firebolt.Core
{
    public class RewriteEngine
    {
        GitGraph<CommitMetadata> graph;
        Filters filters;
        IRepository repo;
        private Dictionary<string, CommitMetadata> commitMap;


        public RewriteEngine(GitGraph<CommitMetadata> graph, Filters filters, Dictionary<string, CommitMetadata> commitMap, IRepository repo)
        {
            this.graph = graph;
            this.commitMap = commitMap;
            this.repo = repo;
            this.filters = filters;
        }

        public void Run()
        {
            rewriteMetadata();
            rewriteParents();
        }

        private void rewriteMetadata()
        {
            var metadataRewriter = new CommitMetadataRewriter(repo, filters.MetadataFilters, commitMap);
            var toRemove = metadataRewriter.Run(graph.Vertices.AsParallel().Where(c => !c.IsBoundary));
            
            // Now patch our graph
            foreach (var commit in toRemove)
            {
                graph.RemoveCommit(commit);
            }
        }

        private void rewriteParents()
        {
            var parentRewriter = new ParentRewriter(repo, filters.ParentFilters, commitMap, graph);
            parentRewriter.Run();
        }
    }

    public class Filters
    {
        public IReadOnlyList<ICommitFilter> MetadataFilters { get; }
        public IReadOnlyList<IParentFilter> ParentFilters { get; }

        public Filters(IEnumerable<ICommitFilter> metadataFilters = null, IEnumerable<IParentFilter> parentFilters = null)
        {
            MetadataFilters = metadataFilters?.ToList() ?? new List<ICommitFilter>();
            ParentFilters = parentFilters?.ToList() ?? new List<IParentFilter>();
        }
    }
}
