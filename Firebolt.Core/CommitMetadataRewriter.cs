using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt.Core
{
    public interface ICommitFilter
    {
        bool FilterCommit(CommitMetadata commit, FilterContext context);
    }

    // Rewrites commit metadata in isolation with no regard to parents or children. Highly parallelizable
    class CommitMetadataRewriter
    {
        List<ICommitFilter> filters;
        FilterContext context;

        public CommitMetadataRewriter(IRepository repo, IEnumerable<ICommitFilter> filters, Dictionary<string, CommitMetadata> commitMap)
        {
            context = new FilterContext(repo, commitMap);
            this.filters = filters.ToList();
        }

        public HashSet<CommitMetadata> Run(IEnumerable<CommitMetadata> commitsToRewrite)
        {
            var droppedCommits = commitsToRewrite
                .AsParallel()
                .Where(rewrite);
            return droppedCommits.ToSet();
        }

        bool rewrite(CommitMetadata commit)
        {
            foreach (var f in filters)
            {
                // If filter returns false, drop this commit
                if (!f.FilterCommit(commit, context))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
