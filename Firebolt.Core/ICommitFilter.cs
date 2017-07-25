using LibGit2Sharp;
using System;
using System.Collections.Generic;

namespace Firebolt.Core
{
    public interface ICommitFilter
    {
        IEnumerable<FireboltCommit> FilterCommit(FireboltCommit commit, Commit original, IRepository repo);
    }

    public interface IGraphFilter
    {
        void FilterRewrittenParents(GitGraph graph, IRepository repo);
    }
}