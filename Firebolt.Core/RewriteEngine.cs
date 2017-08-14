using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Firebolt.Core
{
    public struct Progress
    {
        public int Done;
        public int Loaded;
        public int Filtered;
        public int ParentFiltered;
        public int Saved;
        public int Dropped;
    }

    public sealed class RewriteEngine
    {
        private static readonly System.Threading.Tasks.Schedulers.IOTaskScheduler ioScheduler = new System.Threading.Tasks.Schedulers.IOTaskScheduler();

        Filters filters;
        IRepository repo;
        private Dictionary<string, Task<CommitMetadata>> commitMap;
        private Dictionary<string, Task<Commit>> rewrittenMap;
        private FilterContext metaCtx;
        private ParentFilterContext parentCtx;

        private bool pruneEmpty, pruneMerges, pruneMergesAggressive;

        public int Total => rewrittenMap?.Count ?? 0;

        private Progress _progress;
        public Progress Progress => _progress;

        private async Task<IEnumerable<Commit>> Map(string sha)
        {
            if (!commitMap.ContainsKey(sha))
            {
                // Not in the set to rewrite (that were preloaded)
                return new[] { repo.Lookup<Commit>(sha) };
            }

            // Wait for it to be rewritten
            var rewritten = await rewrittenMap[sha];
            if (rewritten != null)
            {
                return new[] { rewritten };
            }

            // It was dropped, so it "becomes" its (rewritten) parents
            return (await commitMap[sha].ConfigureAwait(false)).Parents;
        }

        private async Task<IEnumerable<Commit>> GetOriginalParents(CommitMetadata commitMeta)
        {
            var parents = new List<Commit>();

            if (commitMeta.Original == null)
            {
                return parents;
            }

            foreach (var parent in commitMeta.Original.Parents)
            {
                if (commitMap.ContainsKey(parent.Sha))
                {
                    var rewrittenParent = await Map(parent.Sha).ConfigureAwait(false);
                    if (rewrittenParent != null)
                    {
                        parents.AddRange(rewrittenParent);
                    }
                }
                else
                {
                    // Not being rewritten
                    parents.Add(parent);
                }
            }

            return parents;
        }

        public RewriteEngine(Filters filters, IRepository repo, bool pruneEmpty, bool pruneMerges, bool pruneMergesAggr)
        {
            this.repo = repo;
            this.filters = filters;
            this.metaCtx = new FilterContext(repo);
            this.parentCtx = new ParentFilterContext(repo, this.Map);

            this.pruneEmpty = pruneEmpty;
            this.pruneMerges = pruneMerges;
            this.pruneMergesAggressive = pruneMergesAggr;
        }

        public async Task<Dictionary<string, Commit>> Run(IEnumerable<string> commitsToRewrite)
        {
            _progress = new Progress();
            commitMap = commitsToRewrite
                .ToDictionary(sha => sha, sha => Task.Factory.StartNew(() =>
                {
                    var x = new CommitMetadata(repo.Lookup<Commit>(sha));
                    System.Threading.Interlocked.Increment(ref _progress.Loaded);
                    return x;
                }, new System.Threading.CancellationToken(), TaskCreationOptions.LongRunning, ioScheduler));

            var rewriterTasks = commitMap.Keys
                .ToDictionary(sha => sha, sha => new Task<Task<Commit>>(() => rewrite(sha)));

            rewrittenMap = rewriterTasks
                .ToDictionary(e => e.Key, e => e.Value.Unwrap().ContinueWith(t =>
                {
                    System.Threading.Interlocked.Increment(ref _progress.Done);
                    return t.Result;
                }));

            // Now start our tasks
            Parallel.ForEach(rewriterTasks.Values, t => t.Start());
            await Task.WhenAll(rewrittenMap.Values);

            return rewrittenMap
                .ToDictionary(e => e.Key, e => e.Value.Result);
        }

        private bool runMetadataFilters(CommitMetadata meta)
        {
            foreach (var f in filters.MetadataFilters)
            {
                if (f.FilterCommit(meta, metaCtx) == false)
                {
                    return false; // Drop
                }
            }

            return true;
        }

        private async Task<bool> runParentFilters(CommitMetadata meta)
        {
            foreach (var f in filters.ParentFilters)
            {
                if (await f.FilterParents(meta, parentCtx).ConfigureAwait(false) == false)
                {
                    return false; // drop!
                }
            }
            return true;
        }

        private async Task<Commit> rewrite(string sha)
        {
            var meta = await commitMap[sha].ConfigureAwait(false);

            var keep = runMetadataFilters(meta);
            System.Threading.Interlocked.Increment(ref _progress.Filtered);

            // Set filtered parents before potentially dropping
            meta.Parents = (await GetOriginalParents(meta).ConfigureAwait(false)).ToList();

            if (!keep)
            {
                System.Threading.Interlocked.Increment(ref _progress.Dropped);
                return null;
            }


            if (filters.ParentFilters.Count != 0)
            {
                keep = await runParentFilters(meta).ConfigureAwait(false);
            }

            System.Threading.Interlocked.Increment(ref _progress.ParentFiltered);

            if (!keep)
            {
                System.Threading.Interlocked.Increment(ref _progress.Dropped);
                return null;
            }

            // save
            var savedTree = meta.Tree.Equals(meta.Original?.Tree) ?
                meta.Original?.Tree :
                await Task.Factory.StartNew(() => repo.ObjectDatabase.CreateTree(meta.Tree), new System.Threading.CancellationToken(), TaskCreationOptions.None, ioScheduler).ConfigureAwait(false);

            if (pruneEmpty)
            {
                if (pruneEmptyFilter(meta, savedTree) == false)
                {
                    System.Threading.Interlocked.Increment(ref _progress.Dropped);
                    return null;
                }
            }

            var savedCommit = await Task.Factory.StartNew(() =>
            {
                return repo.ObjectDatabase.CreateCommit(meta.Author.EnsureNonEmpty(), meta.Committer.EnsureNonEmpty(), meta.Message, savedTree, meta.Parents, false);
            }, new System.Threading.CancellationToken(), TaskCreationOptions.None, ioScheduler)
            .ConfigureAwait(false);

            System.Threading.Interlocked.Increment(ref _progress.Saved);

            return savedCommit;
        }

        public bool pruneEmptyFilter(CommitMetadata commit, Tree savedTree)
        {
            // Re-process merges to eliminate branches that don't contribute anything to the tree
            if (commit.Parents.Count > 1 && pruneMergesAggressive)
            {
                var treeSameTo = commit.Parents.Where(p => savedTree.Equals(p.Tree)).FirstOrDefault();

                if (treeSameTo != null)
                {
                    // eliminate the parents that are not treesame as they contribute nothing
                    commit.Parents = commit.Parents.Where(p => p.Tree.Equals(treeSameTo.Tree)).Distinct().ToList();

                    // If it's demoted from a merge, it's a pointless commit now
                    // as it's treesame to its only parent. So dump out early and drop it
                    if (commit.Parents.Count == 1)
                    {
                        return false;
                    }
                }
            }

            if (commit.Parents.Count == 2 && pruneMerges)
            {
                // Heuristic to quickly eliminate the common case of a triangle
                var p1 = commit.Parents[0];
                var p2 = commit.Parents[1];

                if (p2.Parents.Contains(p1))
                {
                    // p1 is redundant as it's reachable from p2
                    commit.Parents.Remove(p1);
                }
                else if (p1.Parents.Contains(p2))
                {
                    // p2 is redundant since reachable from p1
                    commit.Parents.Remove(p2);
                }
            }

            if (commit.Parents.Count > 1 && pruneMerges)
            {
                commit.Parents = GetIndependent(commit.Parents.Distinct());
            }

            if (commit.Parents.Count == 0)
            {
                return savedTree.Count != 0; // Don't include null trees if we're a root
            }

            if (commit.Parents.Count == 1 && savedTree.Equals(commit.Parents[0].Tree))
            {
                return false; // Skip unchanged
            }

            return true;
        }

        /// <summary>
        /// Implementation of `git show-branch --indepenent`
        /// 
        /// "Among the <reference>s given, display only the ones that cannot be reached from any other <reference>"
        /// </summary>
        /// <param name="commitsToCheck"></param>
        /// <returns></returns>
        private List<Commit> GetIndependent(IEnumerable<Commit> commitsToCheck)
        {
            var commitList = commitsToCheck.ToList();

            for (var i = commitList.Count - 1; i > 0; --i)
            {
                var first = commitList[i];
                for (var j = commitList.Count - 1; j >= 0; --j)
                {
                    if (i == j) continue;

                    var second = commitList[j];

                    var mergeBase = repo.ObjectDatabase.FindMergeBase(first, second);

                    if (first.Equals(mergeBase))
                    {
                        // First commit (i) is reachable from second (j), so drop i
                        commitList.RemoveAt(i);

                        // No reason to check anything else against this commit
                        j = -1;
                    } else if (second.Equals(mergeBase))
                    {
                        // Second (j) is reachable from first, so drop j
                        commitList.RemoveAt(j);

                        // If this was at a lower index than i, dec i since we shifted one down
                        if (j < i)
                        {
                            --i;
                        }
                    }
                }
            }

            return commitList;
        }
    }

    public interface ICommitFilter
    {
        bool FilterCommit(CommitMetadata commit, FilterContext context);
    }

    public interface IParentFilter
    {
        Task<bool> FilterParents(CommitMetadata commit, ParentFilterContext context);
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

    public class FilterContext
    {
        public IRepository Repo { get; }

        public FilterContext(IRepository repo)
        {
            this.Repo = repo;
        }
    }

    public class ParentFilterContext : FilterContext
    {
        private Func<string, Task<IEnumerable<Commit>>> commitMap;
        public Task<IEnumerable<Commit>> Map(string sha) => commitMap(sha);

        public ParentFilterContext(IRepository repo, Func<string, Task<IEnumerable<Commit>>> commitMap)
            : base(repo)
        {
            this.commitMap = commitMap;
        }
    }
}
