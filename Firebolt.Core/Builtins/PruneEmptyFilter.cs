using LibGit2Sharp;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Firebolt.Core.Builtins
{
    public class PruneEmptyFilter : IParentFilter
    {
        private readonly bool pruneMerges;
		private readonly bool pruneMergesAggressive;

        public PruneEmptyFilter(bool mergePruning, bool aggressiveMergePruning)
        {
			pruneMerges = mergePruning;
			pruneMergesAggressive = aggressiveMergePruning;
		}
		public async Task<bool> FilterParents(CommitMetadata commit, ParentFilterContext context)
        {
            // Re-process merges to eliminate branches that don't contribute anything to the tree
            if (commit.Parents.Count > 1 && pruneMergesAggressive)
            {
                var treeSameTo = commit.Parents.Where(p => commit.Tree.Equals(p.Tree)).FirstOrDefault();

                if (treeSameTo == null)
                {
                    // No parents were treesame, so there's actual content in this merge. Keep it.
                    return true;
                }

                // Else, eliminate the parents that are not treesame as they contribute nothing
                commit.Parents = commit.Parents.Where(p => p.Tree.Equals(treeSameTo.Tree)).Distinct().ToList();

                // If it's demoted from a merge, it's a pointless commit now
                // as it's treesame to its only parent. So dump out early and drop it
                return commit.Parents.Count != 1;
            }

            if (commit.Parents.Count > 1 && pruneMerges)
            {
                var procInfo = new ProcessStartInfo("git", "show-branch --independent " + string.Join(" ", commit.Parents.Select(x => x.Sha)))
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                using (var proc = await RunProcessAsync(procInfo).ConfigureAwait(false))
                {
                    var newParentShas = proc.StandardOutput.ReadToEnd().Split(' ', '\r', '\n').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct();

                    if (proc.ExitCode != 0)
                    {
						throw new Exception("git show-branch failed! " + proc.StandardError);
					}

                    commit.Parents = newParentShas.Select(context.Repo.Lookup<Commit>).ToList();
                }
            }

            if (commit.Parents.Count == 0)
            {
                return !commit.Tree.IsEmpty; // Don't include null trees if we're a root
            }

            if (commit.Parents.Count == 1 && commit.Tree.Equals(commit.Parents[0].Tree))
            {
                return false; // Skip unchanged
            }

            return true;
        }

        private static async Task<Process> RunProcessAsync(ProcessStartInfo info)
        {
            var tcs = new TaskCompletionSource<int>();

            var p = new Process { StartInfo = info, EnableRaisingEvents = true };
            p.Exited += (s, ea) => tcs.SetResult(p.ExitCode);

            p.Start();

            await tcs.Task.ConfigureAwait(false);
            return p;
        }
    }
}
