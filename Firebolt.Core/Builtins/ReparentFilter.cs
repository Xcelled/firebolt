using System.Collections.Generic;
using System.Linq;
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
            if (newParents.ContainsKey(commit.Original?.Sha))
            {
                commit.Parents = (await Task.WhenAll(newParents[commit.Original?.Sha].Select(context.Map))).Flatten().ToList();
                return true;
            }

            return false;
        }
    }
}
