using LibGit2Sharp;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebolt.Core;


// Main idea: we take a rev list like filter branch. Instead of trying to parse it ourselves,
// shell out to rev-list and load the resulting commits. Rewrite these in parallel
// Once we've done that, commit the rewritten commits. Then, shell out to git-rev-list with a list
// of the rewritten commits and the --simplify-merges flag. Git will give us back a tree with all
// the redudnant stuff taken out. It's easier, faster, and more reliable to call git for this info
// rather than try to implement it ourselves. Once we have the history simplification,
// start the rewriter again but only to change the parents. Once that's done, persist the new commits
// and finally update any refs that changed.

namespace Firebolt
{
    class Main1
    {
        static Tuple<string, string> parseSubdirectoryOption(string option)
        {
            var parts = option.Split(new char[] { ':' }, 2);

            var from = parts[0].Trim();
            var to = parts.Length == 2 ? parts[1] : "";

            return Tuple.Create(from, to);
        }

        static void Main(string[] args)
        {
            var showHelp = false;
            var pruneEmpty = false;
            var subdirectoryFilters = new List<Tuple<string, string>>();
            var revListOptions = new List<string>();

            var optionDefs = new OptionSet
            {
                { "prune-empty", "", v => pruneEmpty = v != null },
                { "subdirectory-filter=", "", v => subdirectoryFilters.Add(parseSubdirectoryOption(v)) }
            };

            try
            {
                revListOptions = optionDefs.Parse(args);
                if (revListOptions.Count == 0)
                {
                    throw new Exception("Must specify a branch name!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                Console.WriteLine("Usage: FireBolt [OPTIONS] [--] [rev-list options]");
                Console.WriteLine();
                Console.WriteLine("Options:");
                optionDefs.WriteOptionDescriptions(Console.Out);
                Environment.Exit(1);
            }

            var filters = new List<ICommitFilter>();

            // Builtins
        }

        private static void updateRef(Repository repo, Reference reference, Dictionary<Commit, HashSet<Commit>> rewrites)
        {
            var oldTip = reference.ResolveToDirectReference().Target.Sha;
            var firstAncestor = repo.Commits.QueryBy(new CommitFilter
            {
                FirstParentOnly = false,
                IncludeReachableFrom = oldTip,
                SortBy = CommitSortStrategies.Topological
            }).Where(c => rewrites.ContainsKey(c) && rewrites[c].Count != 0).FirstOrDefault();

            if (firstAncestor == null)
            {
                Console.WriteLine($"WARNING: CANNOT FIND REWRITTEN ANCESTOR FOR {reference.CanonicalName}");
                return;
            }

            HashSet<Commit> newTip = rewrites[firstAncestor];

            if (newTip.Count != 1)
            {
                Console.WriteLine($"WARNING: {reference.CanonicalName} rewritten into multiple commits. Using the first:");
                foreach (var c in newTip)
                {
                    Console.WriteLine($" - {c.Sha}");
                }
            }

            var newCommit = newTip.First();

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

        //private static Dictionary<Commit, HashSet<Commit>> runRewriter(Repository repo, RewriteEngine rewriter, HashSet<Commit> toRewrite)
        //{
        //    Console.WriteLine("Rewriting commits...");
        //    var overallTimer = new Stopwatch();

        //    var resultsTask = Task.Run(() => rewriter.Rewrite(toRewrite));

        //    overallTimer.Start();

        //    while (resultsTask.Status != TaskStatus.RanToCompletion && resultsTask.Status != TaskStatus.Faulted)
        //    {
        //        System.Threading.Thread.Sleep(1000);
        //        if (rewriter.Total == 0 || rewriter.Done == 0)
        //        {
        //            continue;
        //        }

        //        var rate = rewriter.Done / overallTimer.Elapsed.TotalMilliseconds;

        //        var totalTime = TimeSpan.FromMilliseconds(rewriter.Total / rate);

        //        Console.Write($"\r{new string(' ', Console.WindowWidth-1)}\rRewrote {rewriter.Done} of {rewriter.Total} (est {totalTime - overallTimer.Elapsed} remaining)");
        //    }

        //    overallTimer.Stop();

        //    Console.WriteLine();
        //    Console.WriteLine($"Rewrote {rewriter.Done} of {rewriter.Total} in {overallTimer.Elapsed} ({rewriter.Total / (int)overallTimer.Elapsed.TotalSeconds} commits/s)");

        //    var results = resultsTask.Result;
        //    return results;
        //}

        //private static string escape(IEnumerable<string> args)
        //{
        //    return string.Join(" ", args.Select(arg =>
        //    {
        //        var s = Regex.Replace(arg, @"(\\*)" + "\"", @"$1$1\" + "\"");
        //        s = "\"" + Regex.Replace(s, @"(\\+)$", @"$1$1") + "\"";
        //        return s;
        //    }));
        //}
    }
}
