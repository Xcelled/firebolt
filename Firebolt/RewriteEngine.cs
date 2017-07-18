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
        List<ICommitLimiter> limiters;
        List<IFilter> filters;
        IRepository repository;

        Dictionary<Commit, Task<CommitSet>> rewriters;

        RewriteOptions options;

        public int Total => rewriters?.Count ?? 0;
        public int Done => rewriters?.Values?.Count(t => t.IsCompleted) ?? 0;

        public RewriteEngine(IRepository repo, IEnumerable<IFilter> filters, IEnumerable<ICommitLimiter> limiters, RewriteOptions options)
        {
            repository = repo;
            this.filters = filters.ToList();
            this.limiters = limiters.ToList();
            this.options = options;
        }

        public Task<Dictionary<Commit, CommitSet>> Rewrite(CommitSet commits)
        {
            var toRewriteEnum = (IEnumerable<Commit>)commits;

            foreach (var f in limiters)
            {
                toRewriteEnum = f.Limit(toRewriteEnum);
            }

            // Only rewrite commits in toRewrite
            // The rest should be "remapped" to their (rewritten) parents
            var toRewrite = new HashSet<Commit>(toRewriteEnum);

            Debug.WriteLine("Given {0} commits to rewrite. Will actually rewrite {1}, while {2} are remapped.", commits.Count, toRewrite.Count, commits.Count - toRewrite.Count);

            // Prepare our rewriting tasks (but don't start them)
            var rewriterTasks = commits
                .AsParallel()
                .ToDictionary(c => c, c =>
                {
                    if (toRewrite.Contains(c))
                    {
                        // Not excluded, rewrite it
                        return new Task<Task<CommitSet>>(() => rewrite(c));
                    }
                    // Was excluded by filters, so remap it to rewritten parents
                    return new Task<Task<CommitSet>>(() => getRewrittenParentsOnly(c));
                });

            // Unwrap the each rewriter task, giving us a dictionary commit -> Task<Rewritten Commit>
            rewriters = rewriterTasks.AsParallel().ToDictionary(e => e.Key, e => e.Value.Unwrap());

            // Now start our rewriters
            Parallel.ForEach(rewriterTasks.Values, t => t.Start());

            // When everything's been rewritten, return the results
            return Task.WhenAll(rewriters.Values)
                .ContinueWith(t => rewriters.AsParallel().ToDictionary(e => e.Key, e => e.Value.Result));
        }

        private Task<CommitSet> getRewrittenOrOriginalParents(Commit commit)
        {
            return Task.WhenAll(commit.Parents.Select(mapToRewrittenOrOriginal))
                    .ContinueWith(t => t.Result.Flatten().ToCommitSet(), TaskContinuationOptions.ExecuteSynchronously);
        }
        private Task<CommitSet> getRewrittenParentsOnly(Commit commit)
        {
            return Task.WhenAll(commit.Parents.Select(mapToRewrittenOnly))
                    .ContinueWith(t => t.Result.Flatten().ToCommitSet(), TaskContinuationOptions.ExecuteSynchronously);
        }

        private Task<CommitSet> mapToRewrittenOrOriginal(Commit commit)
        {
            if (rewriters.ContainsKey(commit))
                return rewriters[commit];
            else
                return Task.FromResult(new CommitSet { commit });
        }

        private Task<CommitSet> mapToRewrittenOnly(Commit commit)
        {
            if (rewriters.ContainsKey(commit))
                return rewriters[commit];
            else
                return Task.FromResult(new CommitSet());
        }

        private Task<CommitSet> rewrite(Commit commit)
        {
            var loadParentsTask = getRewrittenOrOriginalParents(commit);
            var loadTreeTask = Task.Run(() => TreeMetadata.From(commit.Tree));

            return Task.WhenAll(loadParentsTask, loadTreeTask)
                .ContinueWith(preloadTask => filterCommit(commit, loadParentsTask.Result, loadTreeTask.Result))
                .Unwrap();
        }

        private Task<CommitSet> filterCommit(Commit original, CommitSet rewrittenParents, TreeMetadata treePreload)
        {
            var originalMetadata = CommitMetadata.From(original, parents: rewrittenParents, tree: treePreload);
            var originalTreeMetadata = originalMetadata.Tree;

            // Rewrite commit metadata
            var filterTask = Task.Run(() =>
            {
                IEnumerable<CommitMetadata> commitMetas = new CommitMetadata[] { originalMetadata };

                foreach (var f in filters)
                {
                    commitMetas = commitMetas.SelectMany(c => f.Run(c, original, repository));
                }

                return commitMetas;
            });

            // Persist the trees
            var writeTreeTask = filterTask.ContinueWith(t =>
            {
                return t.Result.Select(rewrittenMetadata =>
                {
                    var needRewriteTree = !ReferenceEquals(rewrittenMetadata.Tree, originalTreeMetadata);
                    var newTree = needRewriteTree ? repository.ObjectDatabase.CreateTree(rewrittenMetadata.Tree) : original.Tree;
                    return new { CommitInfo = rewrittenMetadata, Tree = newTree };
                });
            });

            if (options.PruneEmpty)
            {
                writeTreeTask = writeTreeTask.ContinueWith(t => t.Result.Where(treeData =>
                {
                    if (rewrittenParents.Count > 1)
                        return true; // It's a merge, keep it
                    if (rewrittenParents.Count == 1 && treeData.Tree == rewrittenParents.First().Tree)
                        return false; // Skip unchanged
                    if (rewrittenParents.Count == 0 && treeData.Tree.Count == 0)
                        return false; // Don't include null trees if we're the root

                    return true;
                }));
            }

            // Persist the commits
            var writeCommitTask = writeTreeTask.ContinueWith(t =>
            {
                var rewrittenCommits = t.Result.Select(meta => repository.ObjectDatabase.CreateCommit(meta.CommitInfo.Author.EnsureNonEmpty(), meta.CommitInfo.Committer.EnsureNonEmpty(),
                            meta.CommitInfo.Message, meta.Tree, meta.CommitInfo.Parents, false))
                            .ToCommitSet();

                // If it's not written into anything, it's being eliminated so return its parents instead  (skipping this commit)
                return rewrittenCommits.Count == 0 ? rewrittenParents : rewrittenCommits;
            });


            return writeCommitTask;
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

            return new Signature(blankName ? "Unknown" : sig.Name, blankEmail ? "Unknown" : sig.Email, sig.When);
        }

        public static CommitSet ToCommitSet(this IEnumerable<Commit> @enum)
        {
            return new CommitSet(@enum);
        }

        public static IEnumerable<T> Flatten<T>(this IEnumerable<IEnumerable<T>> @enum)
        {
            return Enumerable.SelectMany(@enum, x => x);
        }
    }
}
