using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt.Core
{
    public class FilterContext
    {
        private Dictionary<string, CommitMetadata> commitMap;

        public IRepository Repo { get; }

        public CommitMetadata Lookup(string sha) => commitMap.ContainsKey(sha) ? commitMap[sha] : null;

        public FilterContext(IRepository repo, Dictionary<string, CommitMetadata> commitMap)
        {
            this.Repo = repo;
            this.commitMap = commitMap;
        }
    }
}
