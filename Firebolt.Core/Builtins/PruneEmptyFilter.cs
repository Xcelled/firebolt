using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt.Core.Builtins
{
    public class PruneEmptyFilter : IParentFilter
    {
        public async Task<bool> FilterParents(CommitMetadata commit, ParentFilterContext context)
        {
            var parents = await context.WaitForRewrittenParents(commit);

            if (parents.Length == 0 && commit.Tree.IsEmpty)
            {
                return false; // Don't include null trees if we're a root
            }

            if (parents.Length == 1 && commit.Tree.Equals(parents[0].Tree))
            {
                return false; // Skip unchanged
            }

            return true;
        }
    }
}
