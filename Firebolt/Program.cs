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


namespace Firebolt
{
    class Program
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
            Run(args);
            //try
            //{
            //    Run(args);
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine($"fatal: {ex.Message}");
            //    Environment.Exit(1);
            //}
        }

        static void Run(string[] args)
        {
            var showHelp = false;
            var pruneEmpty = false;
            var subdirectoryFilters = new List<Tuple<string, string>>();
            var scriptArgs = args.TakeWhile(x => x != "--");
            var revListOptions = args.SkipWhile(x => x != "--").Skip(1).ToList();

            var optionDefs = new OptionSet
            {
                { "prune-empty", "Remove empty commits", v => pruneEmpty = v != null },
                { "subdirectory-filter=", "Move subdirectories", v => subdirectoryFilters.Add(parseSubdirectoryOption(v)) },
                { "?|help", "Show this screen.", v => showHelp = v != null }
            };

            try
            {
                revListOptions.AddRange(optionDefs.Parse(args));
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

            var metadataFilters = new List<ICommitFilter>();
            var parentFilters = new List<IParentFilter>();

            // Builtins
            if (subdirectoryFilters.Count != 0)
            {
                metadataFilters.Add(new Core.Builtins.SubdirectoryFilter(subdirectoryFilters.ToDictionary(x => x.Item1, x => x.Item2)));
            }
            if (pruneEmpty)
            {
                parentFilters.Add(new Core.Builtins.PruneEmptyFilter());
            }

            using (var repo = new Repository(Environment.CurrentDirectory))
            {
                var fb = new Firebolt(repo, new FireboltOptions(new Filters(metadataFilters, parentFilters), revListOptions, true));
                fb.Run();
            }
        }
    }
}
