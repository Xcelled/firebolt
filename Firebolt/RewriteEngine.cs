using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using CommitSet = System.Collections.Generic.HashSet<LibGit2Sharp.Commit>;

namespace Firebolt
{
	class RewriteEngine
	{
		List<ICommitFilter> commitFilters;
		List<ICommitParentFilter> parentFilters;
		IRepository repository;
		RewriteOptions options;

		Dictionary<Commit, Task<CommitSet>> rewrittenCommits;

		public int Total => rewrittenCommits?.Count ?? 0;
		public int Done => rewrittenCommits?.Values?.Count(t => t.IsCompleted) ?? 0;

		public RewriteEngine(IRepository repo, IEnumerable<ICommitFilter> commitFilters, IEnumerable<ICommitParentFilter> parentFilters, RewriteOptions options)
		{
			repository = repo;
			this.commitFilters = commitFilters.ToList();
			this.parentFilters = parentFilters.ToList();
			this.options = options;
		}

		public Task<Dictionary<Commit, CommitSet>> Rewrite(CommitSet commits)
		{
			// Prepare our rewriting tasks (but don't start them)
			var rewriterTasks = commits
				.AsParallel()
				.ToDictionary(c => c, c => new Task<Task<CommitSet>>(() => rewrite(c)));

			// Unwrap the each rewriter task, giving us a dictionary commit -> Task<Rewritten Commit>
			rewrittenCommits = rewriterTasks.AsParallel().ToDictionary(e => e.Key, e => e.Value.Unwrap());

			// Now start our rewriters
			Parallel.ForEach(rewriterTasks.Values, t => t.Start());

			// When everything's been rewritten, return the results
			return Task.WhenAll(rewrittenCommits.Values)
				.ContinueWith(t => rewrittenCommits.AsParallel().ToDictionary(e => e.Key, e => e.Value.Result));
		}

		private Task<CommitSet> getRewrittenOrOriginalParents(Commit commit)
		{
			return Task.WhenAll(commit.Parents.Select(mapToRewrittenOrOriginal))
					.ContinueWith(t => t.Result.Flatten().ToSet(), TaskContinuationOptions.ExecuteSynchronously);
		}
		private Task<CommitSet> getRewrittenParentsOnly(Commit commit)
		{
			return Task.WhenAll(commit.Parents.Select(mapToRewrittenOnly))
					.ContinueWith(t => t.Result.Flatten().ToSet(), TaskContinuationOptions.ExecuteSynchronously);
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

		private Task<CommitSet> rewrite(Commit originalCommit)
		{
			var loadParentsTask = getRewrittenOrOriginalParents(originalCommit);

			var original = FireboltCommit.From(originalCommit);
			var originalTreeMetadata = original.Tree;

			var filterCommitTask = Task.Run(() => filterCommit(original, originalCommit));

			var reparentTask = Task.WhenAll(loadParentsTask, filterCommitTask)
				.ContinueWith(t => filterCommitTask.Result.Select(c => FireboltCommit.From(c, parents: loadParentsTask.Result)).ToSet());

			if (parentFilters.Count != 0)
			{
				var filterParentsFunc = new Func<FireboltCommit, HashSet<FireboltCommit>>(c => filterParents(c, originalCommit));
				reparentTask = reparentTask
					.ContinueWith(t => filterCommitTask.Result.SelectMany(filterParentsFunc).ToSet());
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
                        return true; // It's a merge, keep it
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

	class RewriteOptions
	{
		public bool PruneEmpty { get; }
		public RewriteOptions(bool pruneEmpty = true)
		{
			this.PruneEmpty = pruneEmpty;
		}
	}

	static class Extensions
	{
		public static Signature EnsureNonEmpty(this Signature sig, string defaultName = "Unknown", string defaultEmail = "Unknown")
		{
			var blankName = string.IsNullOrEmpty(sig.Name);
			var blankEmail = string.IsNullOrEmpty(sig.Email);

			if (!blankEmail && !blankName)
			{
				return sig;
			}

			return new Signature(blankName ? defaultName : sig.Name, blankEmail ? defaultEmail : sig.Email, sig.When);
		}

		public static HashSet<T> ToSet<T>(this IEnumerable<T> @enum)
		{
			var casted = @enum as HashSet<T>;
			if (casted != null)
			{
				return casted;
			}

			return new HashSet<T>(@enum);
		}

		public static IEnumerable<T> Flatten<T>(this IEnumerable<IEnumerable<T>> @enum)
		{
			return Enumerable.SelectMany(@enum, x => x);
		}
	}
}
