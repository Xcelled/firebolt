using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebolt.Core;

namespace Firebolt
{
    /// <summary>
    /// Shell helpers for calling out to Git for things that libgit can't quite do yet
    /// </summary>
    static class Git
    {
        public static IEnumerable<string> RevParse(IEnumerable<string> args)
        {
            var procInfo = GitStartInfo("rev-parse", args);
            procInfo.RedirectStandardOutput = true;

            var proc = Process.Start(procInfo);

            foreach (var line in proc.StandardOutput.ReadLines())
            {
                yield return line;
            }

            proc.EnsureSuccessful();
        }

        public static IEnumerable<string> RevList(IEnumerable<string> args)
        {
            var procInfo = GitStartInfo("rev-list", args);
            procInfo.RedirectStandardOutput = true;

            var proc = Process.Start(procInfo);

            foreach (var line in proc.StandardOutput.ReadLines())
            {
                yield return line;
            }

            proc.EnsureSuccessful();
        }

        public static IEnumerable<string> RevList(IEnumerable<string> args, IEnumerable<string> stdin)
        {
            var procInfo = GitStartInfo("rev-list", args.Concat("--stdin"));
            procInfo.RedirectStandardOutput = true;
            procInfo.RedirectStandardInput = true;

            var proc = Process.Start(procInfo);
            foreach (var line in stdin)
            {
                proc.StandardInput.WriteLine(line);
            }
            proc.StandardInput.Close();

            foreach (var line in proc.StandardOutput.ReadLines())
            {
                yield return line;
            }

            proc.EnsureSuccessful();
        }

        private static void EnsureSuccessful(this Process p)
        {
            if (!p.HasExited)
            {
                throw new Exception("Expected process to have exited: " + p.StartInfo);
            }

            if (p.ExitCode != 0)
            {
                throw new Exception("Process `" + p.StartInfo.FileName + " " + p.StartInfo.Arguments + "` exited with nonzero code: " + p.StandardError.ReadToEnd());
            }
        }

        private static IEnumerable<string> ReadLines(this StreamReader sr)
        {
            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                if (!string.IsNullOrEmpty(line))
                {
                    yield return line;
                }
            }
        }

        private static ProcessStartInfo GitStartInfo(string command, IEnumerable<string> args)
        {
            return new ProcessStartInfo()
            {
                FileName = "git",
                Arguments = escape(command.ConcatWith(args)),
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };
        }

        private static string escape(IEnumerable<string> args)
        {
            return string.Join(" ", args.Select(arg =>
            {
                var s = Regex.Replace(arg, @"(\\*)" + "\"", @"$1$1\" + "\"");
                s = "\"" + Regex.Replace(s, @"(\\+)$", @"$1$1") + "\"";
                return s;
            }));
        }
    }
}
