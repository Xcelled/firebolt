# FireBolt

A cross-platform, massively-parallel alternative to `git filter-branch` built on top of [LibGit2Sharp](https://github.com/libgit2/libgit2sharp) and .Net's Task Parallel Library.

## Usage
```
Usage: FireBolt [OPTIONS] [--] [rev-list options]

Options:
      --prune-empty          Remove empty commits
      --prune-empty-merges   Remove redundant merges. Implies --prune-empty
      --prune-empty-merges-agr
                             Remove parents that don't contribute anything to
                               the tree. Implies --prune-empty-merges
      --subdirectory-filter=VALUE
                             Move subdirectories
  -?, --help                 Show this screen.
```

### Description

Lets you rewrite Git revision history by rewriting the branches mentioned in the &lt;rev-list options&gt;, applying custom filters on each revision. Those filters can modify each tree (e.g. removing a file) or information about each commit. Otherwise, all information (including original commit times or merge information) will be preserved.

The command will only rewrite the positive refs mentioned in the command line (e.g. if you pass a..b, only b will be rewritten). If you specify no filters, the commits will be recommitted without any changes.

**NOTE**: This command honors `.git/info/grafts` file and refs in the `refs/replace/` namespace. If you have any grafts or replacement refs defined, running this command will make them permanent.

**WARNING!** The rewritten history will have different object names for all the objects and will not converge with the original branch. You will not be able to easily push and distribute the rewritten branch on top of the original branch. Please do not use this command if you do not know the full implications, and avoid using it anyway, if a simple single commit would suffice to fix your problem. (See the "RECOVERING FROM UPSTREAM REBASE" section in [git-rebase](https://git-scm.com/docs/git-rebase) for further information about rewriting published history.)

Always verify that the rewritten version is correct: The original refs, if different from the rewritten ones, will be stored in the namespace refs/original/.

Note that since this operation is very I/O expensive, it might be a good idea to locate the repository off-disk, e.g. on tmpfs. Reportedly the speedup is very noticeable.

### Options

 - **--prune-empty**<br>Some filters will generate empty commits that leave the tree untouched. This option instructs Firebolt to remove such commits if they have exactly one or zero non-pruned parents; merge commits will therefore remain intact.  For example:<br><pre>      D--E--F--G<br>    /     /<br>A--B-----C</pre> If C and E are rendered empty, they would be removed and the resulting history would be:<br><pre>      D<br>    /   \\<br>A--B-----F--G</pre>

 - **--prune-empty-merges**<br>Like `--prune-empty`, except merge parents will be removed if they are an ancestor of another parent. Merges reduced to an empty commit with a single parent will be removed. This can be used to prune away topic branches that are rendered empty by filters. For example:<br><pre>      D--E--F--G<br>    /     /<br>A--B-----C</pre>If C and E are rendered empty, they will be removed as described under `--prune-empty`. This option will then cause the link between F and B to be broken, since that side of the merge now points to B **and** B is reachable from the other parent, D. The resulting history will be:<br><pre>A--B--D--F--G</pre>If after this process F is now an *empty* commit with a single parent, as is the usual case for merging a topic branch, it will also be removed. The resulting history would then be:<br><pre>A--B--D--G</pre>Note that this is the same as if the topic branch containing C was never created.

 - **--prune-empty-merges-agr**<br>Like `--prune-empty-merges` but will summarily discard any parents that are not TREESAME to the merge result if there is at least one TREESAME parent. In other words, if the merge was resolved such that no changes from a parent were kept, drop the parent and any unique ancestors of that parent from the rewritten history.<br><br>This results in a significant speedup but has the potential to eliminate history of a branch that was merged in but retains no visible effects or duplicates work that was merged earlier. For example:<br><pre>      E--F--G--H<br>    /     /<br>A--B--C--D</pre>If G was created such that it is TREESAME to F but not D (for example, by discarding all changes from D during the merge) then D (and by extension C) will be dropped *even if C contains interesting history*. The resulting history will be:<br><pre>A--B--E--F--H</pre>

 - **--subdirectory-filter &lt;directory&gt; | [from]:to**<br>Used to move subdirectories similar to [git filter-branch](https://git-scm.com/docs/git-filter-branch#git-filter-branch---subdirectory-filterltdirectorygt) but supporting additional syntax. Instead of specifying a single path to be used as the new root, you can specify a `from` path and a `to` path separated by a colon. `from` may be omitted to indicate the root tree. Paths not matching `from` or `directory` will be eliminated.
