using LibGit2Sharp;
using System;
using System.Collections.Generic;

namespace Firebolt
{
    public interface ICommitFilter
    {
        IEnumerable<FireboltCommit> FilterCommit(FireboltCommit commit, Commit original, IRepository repo);
    }

    public interface ICommitParentFilter
    {
        IEnumerable<FireboltCommit> FilterRewrittenParents(FireboltCommit commit, Commit original, IRepository repo);
    }
}