﻿using LibGit2Sharp;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using Firebolt.Core;
using Firebolt.Core.Builtins;

namespace Firebolt
{
    class Program
    {
        static KeyValuePair<string, string> parseSubdirectoryOption(string option)
        {
            var parts = option.Split(new char[] { ':' }, 2);

            var from = parts[0];
            var to = parts.Length == 2 ? parts[1] : null;

            return new KeyValuePair<string, string>(from, to);
        }

        static void Main(string[] args)
        {
#if DEBUG
            Run(args);
#else
            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"fatal: {GetFirstRealException(ex).Message}");
                Environment.Exit(1);
            }
#endif
        }

        static void Run(string[] args)
        {
            var showHelp = false;
            var pruneEmpty = false;
            var pruneEmptyMerges = false;
            var pruneEmptyMergesAggressive = false;
            var subdirectoryFilters = new List<KeyValuePair<string, string>>();
            var scriptArgs = args.TakeWhile(x => x != "--");
            var revListOptions = args.SkipWhile(x => x != "--").Skip(1).ToList();

            var optionDefs = new OptionSet
            {
                { "prune-empty", "Remove empty commits", v => pruneEmpty = v != null },
                { "prune-empty-merges", "Remove redundant merges. Implies --prune-empty", v => pruneEmptyMerges = v != null },
                { "prune-empty-merges-agr", "Remove parents that don't contribute anything to the tree. Implies --prune-empty-merges", v => pruneEmptyMergesAggressive= v != null },
                { "subdirectory-filter=", "Move subdirectories", v => subdirectoryFilters.Add(parseSubdirectoryOption(v)) },
                { "subdirectory-json=", "Move subdirectories (specified in a JSON dict)", v => subdirectoryFilters.AddRange(parseSubdirFile(v)) },
                { "?|help", "Show this screen.", v => showHelp = v != null }
            };

            try
            {
                revListOptions.AddRange(optionDefs.Parse(scriptArgs));
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

            Console.WriteLine("The following options were not recognized and will be passed to git:");
            foreach (var opt in revListOptions)
            {
                Console.WriteLine(opt);
            }

            pruneEmptyMerges |= pruneEmptyMergesAggressive;
            pruneEmpty |= pruneEmptyMerges;

            var metadataFilters = new List<ICommitFilter>();
            var parentFilters = new List<IParentFilter>();

            // Builtins
            if (subdirectoryFilters.Count != 0)
            {
                var sfd = new SubdirectoryFilter(subdirectoryFilters.ToDictionary(e => e.Key, e => e.Value));
                metadataFilters.Add(sfd);
            }

            if (pruneEmpty)
            {
               // parentFilters.Add(new PruneEmptyFilter(pruneEmptyMerges, pruneEmptyMergesAggressive));
            }

            using (var repo = new Repository(Environment.CurrentDirectory))
            {
                var fb = new Firebolt(repo, new FireboltOptions(new Filters(metadataFilters, parentFilters), revListOptions, pruneEmpty, pruneEmptyMerges, pruneEmptyMergesAggressive));
                fb.Run();
            }
        }

        static IEnumerable<KeyValuePair<string, string>> parseSubdirFile(string path)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText(path));
        }

        private static Exception GetFirstRealException(Exception exception)
        {
            Exception realException = exception;

            if (realException is AggregateException aggregateException)
            {
                realException = aggregateException.Flatten().InnerException; // take first real exception

                while (realException != null && realException.InnerException != null)
                {
                    realException = realException.InnerException;
                }
            }

            return realException ?? exception;
        }
    }
}
