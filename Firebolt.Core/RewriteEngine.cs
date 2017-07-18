using LibGit2Sharp;
using System;
using System.Collections.Generic;
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

    public class RewriteEngine
    {
        protected static System.Threading.Tasks.Schedulers.IOTaskScheduler ioScheduler = new System.Threading.Tasks.Schedulers.IOTaskScheduler();

        Filters filters;
        IRepository repo;
        private Dictionary<string, Task<CommitMetadata>> commitMap;
        private Dictionary<string, Task<Commit>> rewrittenMap;
        private FilterContext metaCtx;
        private ParentFilterContext parentCtx;

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

            // It was dropped, so it "becomes" its parents
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

        public RewriteEngine(Filters filters, IRepository repo)
        {
            this.repo = repo;
            this.filters = filters;
            this.metaCtx = new FilterContext(repo);
            this.parentCtx = new ParentFilterContext(repo, this.Map);
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
            var parentsTask = GetOriginalParents(meta);

            var keep = runMetadataFilters(meta);
            System.Threading.Interlocked.Increment(ref _progress.Filtered);

            if (!keep)
            {
                System.Threading.Interlocked.Increment(ref _progress.Dropped);
                return null;
            }

            meta.Parents = (await parentsTask.ConfigureAwait(false)).ToList();

            keep = await runParentFilters(meta).ConfigureAwait(false);
            System.Threading.Interlocked.Increment(ref _progress.ParentFiltered);

            if (!keep)
            {
                System.Threading.Interlocked.Increment(ref _progress.Dropped);
                return null;
            }

            // save
            var savedCommit = await Task.Factory.StartNew(() =>
            {
                var savedTree = meta.Tree.Equals(meta.Original?.Tree) ? meta.Original?.Tree : repo.ObjectDatabase.CreateTree(meta.Tree);
                return repo.ObjectDatabase.CreateCommit(meta.Author.EnsureNonEmpty(), meta.Committer.EnsureNonEmpty(), meta.Message, savedTree, meta.Parents, false);
            }, new System.Threading.CancellationToken(), TaskCreationOptions.PreferFairness, ioScheduler)
            .ConfigureAwait(false);

            System.Threading.Interlocked.Increment(ref _progress.Saved);

            return savedCommit;
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
