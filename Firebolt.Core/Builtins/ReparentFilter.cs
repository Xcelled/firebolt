using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt.Core.Builtins
{
    public class ReparentFilter : IParentFilter
    {
        private Dictionary<string, IEnumerable<string>> newParents;

        public ReparentFilter(Dictionary<string, IEnumerable<string>> newParents)
        {
            this.newParents = newParents;
        }

        public async Task<bool> FilterParents(CommitMetadata commit, ParentFilterContext context)
        {
            var newParentsForCommit = newParents[commit.Original.Sha];
            var existingParents = (await context.WaitForRewrittenParents(commit)).Select(c => c.Original.Sha);

            var toRemove = existingParents.Except(newParentsForCommit).Select(context.Lookup);
            var toAdd = newParentsForCommit.Except(existingParents).Select(context.Lookup);

            foreach (var p in toRemove)
            {
                context.BreakRelationship(p, commit);
            }
            foreach (var p in toAdd)
            {
                context.AddRelationship(p, commit);
            }

            return true;
        }
    }
}
