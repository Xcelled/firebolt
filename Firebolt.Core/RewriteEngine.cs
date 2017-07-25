using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;
using QuickGraph.Algorithms;

using FireboltCommitSet = System.Collections.Generic.HashSet<Firebolt.Core.FireboltCommit>;

namespace Firebolt.Core
{
    public class RewriteEngine
	{
        IRepository repository;
		RewriteOptions options;
        GitGraph commitsToRewrite, rewrittenCommits;

        // Maps original commits to rewritten ones
        Dictionary<FireboltCommit, Task<FireboltCommitSet>> commitMap;
        // The tasks to do the rewriting
        Dictionary<FireboltCommit, Task<Task<FireboltCommitSet>>> rewriterTasks;

        public int Total => commitMap?.Count ?? 0;
		public int Done => commitMap?.Values?.Count(t => t.IsCompleted) ?? 0;

		public RewriteEngine(IRepository repo, GitGraph commits, RewriteOptions options)
		{
			repository = repo;
			this.options = options;
            commitsToRewrite = commits;
            rewrittenCommits = new GitGraph();

			// Prepare our rewriting tasks (but don't start them)
            rewriterTasks = commitsToRewrite
                .Vertices
                .AsParallel()
                .ToDictionary(c => c, c => new Task<Task<FireboltCommitSet>>(() => rewriteCommit(c)));

            commitMap = rewriterTasks
                .AsParallel()
                .ToDictionary(e => e.Key, e => e.Value.Unwrap());
		}

		public async Task<object> Rewrite()
		{
			// Now start our rewriters
			Parallel.ForEach(rewriterTasks.Values, t => t.Start());

            // Wait for all commits to be rewritten
            await Task.WhenAll(commitMap.Values);

            // When everything's been rewritten, return the results
            return new { Graph = rewrittenCommits, Map = commitMap.AsParallel().ToDictionary(e => e.Key, e => e.Value.Result) };
		}

		private Task<CommitSet> getRewrittenOrOriginalParents(Commit commit)
		{
			return Task.WhenAll(commit.Parents.Select(mapToRewrittenOrOriginal))
					.ContinueWith(t => t.Result.Flatten().ToSet());
		}
		private Task<CommitSet> getRewrittenParentsOnly(Commit commit)
		{
			return Task.WhenAll(commit.Parents.Select(mapToRewrittenOnly))
					.ContinueWith(t => t.Result.Flatten().ToSet());
		}

		private Task<CommitSet> mapToRewrittenOrOriginal(Commit commit)
		{
			if (rewrittenCommits.ContainsKey(commit))
				return rewrittenCommits[commit];
			else
				return Task.FromResult(new CommitSet { commit });
		}

		private Task<CommitSet> mapToRewrittenOnly(Commit commit)
		{
			if (rewrittenCommits.ContainsKey(commit))
				return rewrittenCommits[commit];
			else
				return Task.FromResult(new CommitSet());
		}

        private FireboltCommitSet runCommitFilters(FireboltCommit original)
        {
            return null;
        }

        private async Task<FireboltCommitSet> rewriteCommit(FireboltCommit originalCommit)
        {
            var originalTreeMetadata = originalCommit.Tree;
            var filterTask = Task.Run(() =>runCommitFilters(originalCommit));
            var getParentsTask = getRewrittenOrOriginalParents(originalCommit);

            var filteredCommits = await filterTask;
            var parents = await getParentsTask;

            var relationships = filteredCommits.SelectMany(c => parents.Select(p => new Parentage(c, p)));

            rewrittenCommits.AddVerticesAndEdgeRange(relationships);
		}

		private Task<CommitSet> rewrite(Commit originalCommit)
		{
			var original = FireboltCommit.From(originalCommit);
			var originalTreeMetadata = original.Tree;

			var filterCommitTask = Task.Run(() => filterCommit(original, originalCommit));
			var loadParentsTask = getRewrittenOrOriginalParents(originalCommit);

			var reparentTask = Task.WhenAll(loadParentsTask, filterCommitTask)
				.ContinueWith(t => filterCommitTask.Result.Select(c => FireboltCommit.From(c, parents: loadParentsTask.Result)).ToSet().AsEnumerable());

			if (parentFilters.Count != 0)
			{
				var filterParentsFunc = new Func<FireboltCommit, HashSet<FireboltCommit>>(c => filterParents(c, originalCommit));
				reparentTask = reparentTask
					.ContinueWith(t => filterCommitTask.Result.SelectMany(filterParentsFunc));
			}

			var writeTreesTask = reparentTask.ContinueWith(t => t.Result.Select(rewritten =>
            {
                var needRewriteTree = !ReferenceEquals(rewritten.Tree, originalTreeMetadata);
                var newTree = needRewriteTree ? repository.ObjectDatabase.CreateTree(rewritten.Tree) : originalCommit.Tree;
                return new { CommitInfo = rewritten, Tree = newTree };
            }));

			if (options.PruneEmpty)
			{
				writeTreesTask = writeTreesTask.ContinueWith(t => t.Result.Where(rewritten =>
                {
                    if (rewritten.CommitInfo.Parents.Count > 1)
                        return true;
                    if (rewritten.CommitInfo.Parents.Count == 1 && rewritten.Tree == rewritten.CommitInfo.Parents.First().Tree)
                        return false; // Skip unchanged
                    if (rewritten.CommitInfo.Parents.Count == 0 && rewritten.Tree.Count == 0)
                        return false; // Don't include null trees if we're the root

                    return true;
                }));
			}

			var writeCommitTasks = writeTreesTask.ContinueWith(t =>
			{
				var rewrittenCommits = t.Result.Select(meta => repository.ObjectDatabase.CreateCommit(meta.CommitInfo.Author.EnsureNonEmpty(), meta.CommitInfo.Committer.EnsureNonEmpty(),
							meta.CommitInfo.Message, meta.Tree, meta.CommitInfo.Parents, false))
							.ToSet();

				// If it's not written into anything, it's being eliminated,
                // so return its parents instead  (skipping this commit)
				return rewrittenCommits.Count == 0 ? loadParentsTask.Result : rewrittenCommits;
			});

			return writeCommitTasks;
		}

        private HashSet<FireboltCommit> filterCommit(FireboltCommit commit, Commit original)
		{
			IEnumerable<FireboltCommit> rewrittenCommits = new FireboltCommit[] { commit };
			foreach (var f in commitFilters)
			{
				rewrittenCommits = rewrittenCommits.SelectMany(c => f.FilterCommit(commit, original, repository));
			}

			return rewrittenCommits.ToSet();
		}

		private HashSet<FireboltCommit> filterParents(FireboltCommit commit, Commit original)
		{
			IEnumerable<FireboltCommit> rewrittenCommits = new FireboltCommit[] { commit };
			foreach (var f in parentFilters)
			{
				rewrittenCommits = rewrittenCommits.SelectMany(c => f.FilterRewrittenParents(commit, original, repository));
			}

			return rewrittenCommits.ToSet();
		}
	}

    public class RewriteOptions
    {
        public bool PruneEmpty { get; }
        public IReadOnlyList<ICommitFilter> CommitFilters { get; }
        public IReadOnlyList<ICommitParentFilter> ParentFilters { get; }
		public RewriteOptions(bool pruneEmpty = true, IEnumerable<ICommitFilter> commitFilters = null, IEnumerable<ICommitParentFilter> parentFilters = null)
		{
			this.PruneEmpty = pruneEmpty;
            this.CommitFilters = commitFilters?.ToList() ?? new List<ICommitFilter>();
            this.ParentFilters = parentFilters?.ToList() ?? new List<ICommitParentFilter>();
        }
	}
}
