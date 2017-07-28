using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt.Core
{
    public static class GraphHelper
    {
        public static GitGraph<CommitMetadata> LoadFromCommits(IDictionary<string, CommitMetadata> commits)
        {
            var graph = new GitGraph<CommitMetadata>();

            var edges = commits.Values
                .AsParallel()
                .SelectMany(commit => commit.Original.Parents.Select(parent =>
                {
                    if (commits.ContainsKey(parent.Sha))
                    {
                        // It's in the set to rewrite
                        return new Parentage<CommitMetadata>(commit, commits[parent.Sha]);
                    }
                    else
                    {
                        // This parent is a boundary parent
                        return new Parentage<CommitMetadata>(commit, new BoundaryCommit(parent));
                    }
                }));

            graph.AddVerticesAndEdgeRange(edges);

            return graph;
        }

        public static Dictionary<string, CommitMetadata> LoadCommits(IRepository repository, ISet<string> shas)
        {
            return shas
                .AsParallel()
                .Select(repository.Lookup<Commit>)
                .ToDictionary(c => c.Sha, c => new CommitMetadata(c));
        }

        public static Dictionary<CommitMetadata, Commit> Save(GitGraph<CommitMetadata> graph, IRepository repo)
        {
            return new GraphSaver(graph, repo).Save();
        }
    }

    public class GraphSaver
    {
        private GitGraph<CommitMetadata> graph;
        private IRepository repo;
        private Dictionary<CommitMetadata, Task<Commit>> savers;
        public GraphSaver(GitGraph<CommitMetadata> graph, IRepository repo)
        {
            this.graph = graph;
            this.repo = repo;
        }

        public Dictionary<CommitMetadata, Commit> Save()
        {
            var saversTasks = graph.Vertices
                .AsParallel()
                .ToDictionary(c => c, c => new Task<Task<Commit>>(() => save(c)));

            savers = saversTasks
                .AsParallel()
                .ToDictionary(e => e.Key, e => e.Value.Unwrap());

            Parallel.ForEach(saversTasks.Values, t => t.Start());
            Task.WaitAll(savers.Values.ToArray());

            return savers.ToDictionary(e => e.Key, e => e.Value.Result);
        }

        private async Task<Commit> save(CommitMetadata commit)
        {
            if (commit.IsBoundary)
            {
                // Already saved
                return commit.Original;
            }

            var savedTreeTask = Task.Run(() => commit.Tree.Equals(commit.Original.Tree) ? commit.Original.Tree : repo.ObjectDatabase.CreateTree(commit.Tree));
            var parentsTask = graph.GetParents(commit).Select(p => savers[p]);

            var savedTree = await savedTreeTask;
            var parents = await Task.WhenAll(parentsTask);

            return repo.ObjectDatabase.CreateCommit(commit.Author, commit.Committer, commit.Message, savedTree, parents, false);
        }
    }
}
