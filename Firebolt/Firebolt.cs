using Firebolt.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

            Console.WriteLine($"Found {headsToRewrite.Count}");

            if (headsToRewrite.Count == 0)
            {
                throw new Exception("Found no heads to rewrite");
            }

            var revsToRewrite = Git.RevParse("--revs-only".ConcatWith(options.RevListOptions)).ToList();
            var filteredRevListOptions = Git.RevParse("--no-revs".ConcatWith(options.RevListOptions)).ToList();

            var shasToRewrite = Git.RevList(new[] { "--default", "HEAD" }.Concat(filteredRevListOptions), revsToRewrite).ToSet();

            if (shasToRewrite.Count == 0)
            {
                throw new Exception("Found nothing to rewrite");
            }

            var rewritten = rewrite(shasToRewrite, options.Filters);

            if (options.SimplifyMerges)
            {
                Console.WriteLine("Simplifying merges");
                rewritten = simplifyMerges(revsToRewrite.Select(sha => rewritten[sha].Sha), filteredRevListOptions);
            }

            foreach (var head in headsToRewrite)
            {
                updateRef(head, rewritten);
            }
        }

        private Dictionary<string, Commit> simplifyMerges(IEnumerable<string> rewrittenRevs, IEnumerable<string> filteredRevListOptions)
        {
            var shasToRewrite = Git.RevList(new[] { "--default", "HEAD", "--parents", "--simplify-merges" }.Concat(filteredRevListOptions), rewrittenRevs)
                .Select(line => line.Split(' '))
                .ToDictionary(arr => arr[0], arr => arr.Skip(1).ToArray().AsEnumerable());

            var reparenter = new Filters(parentFilters: new[] { new Core.Builtins.ReparentFilter(shasToRewrite) });

            return rewrite(shasToRewrite.Keys.ToSet(), reparenter);
        }

        private Dictionary<string, Commit> rewrite(ISet<string> shasToRewrite, Filters filters)
        {
            Console.Write("Loading commits...");
            var commitMap = GraphHelper.LoadCommits(repo, shasToRewrite);
            Console.WriteLine(commitMap.Count);
            Console.Write("Loading commits into graph...");
            var graph = GraphHelper.LoadFromCommits(commitMap);
            Console.WriteLine("Done");

            // rewrite
            var rewriter = new RewriteEngine(graph, filters, commitMap, repo);
            rewriter.Run();

            var savedCommits = GraphHelper.Save(graph, repo);

            // Now create a map from old SHA -> New SHA || null
            return shasToRewrite
                .ToDictionary(sha => sha, sha => savedCommits.ContainsKey(commitMap[sha]) ? savedCommits[commitMap[sha]] : null);
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
        public bool SimplifyMerges { get; }
        public IReadOnlyList<string> RevListOptions { get; }
        public Filters Filters { get; }

        public FireboltOptions(Filters filters, IEnumerable<string> revListOptions, bool simplifyMerges)
        {
            this.Filters = filters;
            this.RevListOptions = revListOptions.ToList();
            this.SimplifyMerges = SimplifyMerges;
        }
    }
}
