﻿using Firebolt.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebolt
{
    class Firebolt
    {
        private IRepository repo;
        private FireboltOptions options;

        public Firebolt(IRepository repo, FireboltOptions options)
        {
            this.repo = repo;
            this.options = options;
        }

        public void Run()
        {
            // Main idea: we take a rev list like filter branch. Instead of trying to parse it ourselves,
            // shell out to rev-list and load the resulting commits. Rewrite these in parallel
            // Once we've done that, commit the rewritten commits. Then, shell out to git-rev-list with a list
            // of the rewritten commits and the --simplify-merges flag. Git will give us back a tree with all
            // the redundant stuff taken out. It's easier, faster, and more reliable to call git for this info
            // rather than try to implement it ourselves. Once we have the history simplification,
            // start the rewriter again but only to change the parents. Once that's done, persist the new commits
            // and finally update any refs that changed.

            Console.Write("Finding refs to rewrite... ");
            var headsToRewrite = Git.RevParse(new[] { "--no-flags", "--revs-only", "--symbolic-full-name", "--default", "HEAD" }.Concat(options.RevListOptions))
                .Select(name => repo.Refs[name])
                .Where(r => !r.IsRemoteTrackingBranch)
                .ToList();

            Console.WriteLine($"{headsToRewrite.Count} ({string.Join(", ", headsToRewrite.Select(h => h.CanonicalName))})");

            if (headsToRewrite.Count == 0)
            {
                throw new Exception("Found no heads to rewrite");
            }

            Console.Write("Listing commits to rewrite...");
            var revsToRewrite = Git.RevParse("--revs-only".ConcatWith(options.RevListOptions)).ToList();
            var filteredRevListOptions = Git.RevParse("--no-revs".ConcatWith(options.RevListOptions)).ToList();

            var shasToRewrite = Git.RevList(new[] { "--default", "HEAD", "--simplify-merges" }.Concat(filteredRevListOptions), revsToRewrite).ToSet();

            Console.WriteLine(shasToRewrite.Count);

            if (shasToRewrite.Count == 0)
            {
                throw new Exception("Found nothing to rewrite");
            }

            var rewritten = rewrite(shasToRewrite, options.Filters);

            foreach (var head in headsToRewrite)
            {
                updateRef(head, rewritten);
            }
        }

        private Dictionary<string, Commit> rewrite(ISet<string> shasToRewrite, Filters filters)
        {
            Console.WriteLine("Rewriting...");
            var rewriter = new RewriteEngine(filters, repo, options.PruneEmpty, options.PruneMerges, options.PruneMergesAggr);

            var t = rewriter.Run(shasToRewrite);
            do
            {
                System.Threading.Thread.Sleep(1000);
                var progress = rewriter.Progress;
                Console.Write($"\r{new string(' ', Console.WindowWidth - 1)}\rDone: {progress.Done}/{rewriter.Total}   Loaded: {progress.Loaded}   Filtered: {progress.Filtered}   Parented: {progress.ParentFiltered}   Dropped: {progress.Dropped}   Saved: {progress.Saved}");
            }
            while (!t.IsCompleted);

            Console.WriteLine();
            Console.WriteLine("Done");

            return t.Result;
        }

        private void updateRef(Reference reference, Dictionary<string, Commit> rewritten)
        {
            var oldTip = reference.ResolveToDirectReference().Target.Sha;
            var firstAncestor = repo.Commits.QueryBy(new CommitFilter
            {
                FirstParentOnly = false,
                IncludeReachableFrom = oldTip,
                SortBy = CommitSortStrategies.Topological
            })
            .Where(c => rewritten.ContainsKey(c.Sha) && rewritten[c.Sha] != null)
            .FirstOrDefault();

            if (firstAncestor == null)
            {
                Console.WriteLine($"{reference.CanonicalName} was deleted");
                repo.Refs.Remove(reference);
                return;
            }

            var newCommit = rewritten[firstAncestor.Sha];

            repo.Refs.UpdateTarget(reference.ResolveToDirectReference(), newCommit.Sha);

            var wasUpdated = oldTip != newCommit.Sha;
            if (wasUpdated)
            {
                Console.WriteLine($"{reference.CanonicalName} was rewritten");
            }
            else
            {
                Console.WriteLine($"WARNING: {reference.CanonicalName} unchanged");
            }
        }
    }

    class FireboltOptions
    {
        public IReadOnlyList<string> RevListOptions { get; }
        public Filters Filters { get; }
        public bool PruneEmpty { get; internal set; }
        public bool PruneMerges { get; internal set; }
        public bool PruneMergesAggr { get; internal set; }

        public FireboltOptions(Filters filters, IEnumerable<string> revListOptions, bool pruneEmpty, bool pruneMerges, bool pruneMergesAggr)
        {
            this.Filters = filters;
            this.RevListOptions = revListOptions.ToList();
            this.PruneEmpty = pruneEmpty;
            this.PruneMerges = pruneMerges;
            this.PruneMergesAggr = pruneMergesAggr;
        }
    }
}
