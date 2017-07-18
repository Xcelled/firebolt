using LibGit2Sharp;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt
{
    class Firebolt
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
                { "subdirectory-filter", "", v => subdirectoryFilters.Add(parseSubdirectoryOption(v)) }
            };

            try
            {
                revListOptions = optionDefs.Parse(args);
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

            var filters = new List<IFilter>();
            var limiters = new List<ICommitLimiter>();

            // Builtins
            if (subdirectoryFilters.Any())
            {
                var sdf = new Builtins.SubdirectoryFilter(subdirectoryFilters.ToDictionary(t => t.Item1, t => t.Item2));
                filters.Add(sdf);
                limiters.Add(sdf);
            }



            using (var repo = new Repository(@"D:\ANSYSDev\AnsysDevCode\CodeDV"))
            {
                Console.WriteLine("Rewriting commits...");
                var rewriter = new RewriteEngine(repo, filters, limiters, new RewriteOptions(pruneEmpty: pruneEmpty));

                var stopwatch = new System.Diagnostics.Stopwatch();
                var resultsTask = Task.Run(() => rewriter.Rewrite(new HashSet<Commit>(repo.Branches["test"].Commits)));
                stopwatch.Start();

                while (resultsTask.Status != TaskStatus.RanToCompletion && resultsTask.Status != TaskStatus.Faulted)
                {
                    System.Threading.Thread.Sleep(1000);
                    if (rewriter.Total == 0 || rewriter.Done == 0)
                    {
                        continue;
                    }

                    var rate = rewriter.Done / stopwatch.Elapsed.TotalMilliseconds;

                    var totalTime = TimeSpan.FromMilliseconds(rewriter.Total / rate);

                    Console.Write($"\rRewrote {rewriter.Done} of {rewriter.Total} (est {totalTime - stopwatch.Elapsed} remaining)");
                }
                stopwatch.Stop();

                Console.WriteLine();
                Console.WriteLine($"Rewrote {rewriter.Done} of {rewriter.Total} in {stopwatch.Elapsed}");

                var results = resultsTask.Result;

                var oldRef = repo.Branches["test"].Tip.Sha;
                var newRef = results[repo.Branches["test"].Tip].First().Sha;

                repo.Refs.UpdateTarget(repo.Branches["test"].Reference.ResolveToDirectReference(), newRef);

                var wasUpdated = repo.Branches["test"].Tip.Sha == newRef;

                Console.WriteLine(repo.Branches["test"].CanonicalName + " was rewritten");
            }
        }
    }
}
