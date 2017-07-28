using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickGraph;
using QuickGraph.Algorithms;
using LibGit2Sharp;
using System.Threading;
using System.Collections.Concurrent;

namespace Firebolt.Core
{
    public sealed class Parentage<TCommit> : Edge<TCommit>
    {
        // Git-centric renames
		public TCommit Child => Source;
		public TCommit Parent => Target;
		public Parentage(TCommit child, TCommit parent) : base(child, parent)
        {
        }
    }

    public class GitGraph<TCommit> : BidirectionalGraph<TCommit, Parentage<TCommit>>
    {
        private readonly ConcurrentDictionary<TCommit, ReaderWriterLockSlim> locks = new ConcurrentDictionary<TCommit, ReaderWriterLockSlim>();
        public GitGraph(): base(false)
        {
        }

        internal ReaderWriterLockSlim GetSingleLock(TCommit commit)
        {
            return locks.GetOrAdd(commit, x => new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion));
        }

        /// <summary>
        /// Locks a commit and it's relatives (parents and children) for reading.
        /// Returns an object that can be used to lock them for writing
        /// </summary>
        /// <param name="commit"></param>
        /// <returns></returns>
        public CommitLock Lock(TCommit commit)
        {
            var commitLock = GetSingleLock(commit);
            commitLock.EnterUpgradeableReadLock();

            var parents = GetParents(commit);
            var children = GetChildren(commit);

            var dependentLocks = parents.Concat(children).OrderBy(x => x.GetHashCode()).Select(GetSingleLock).ToArray();
            foreach (var l in dependentLocks)
            {
                l.EnterUpgradeableReadLock();
            }

            return new CommitLock(dependentLocks.Concat(commitLock).ToArray());
        }

        public IEnumerable<TCommit> GetParents(TCommit commit)
        {
            var commitLock = GetSingleLock(commit);
            commitLock.EnterReadLock();
            try
            {
                return OutEdges(commit).Select(e => e.Parent).ToList();
            }
            finally
            {
                commitLock.ExitReadLock();
            }    
        }
        public IEnumerable<TCommit> GetChildren(TCommit commit)
        {
            var commitLock = GetSingleLock(commit);
            commitLock.EnterReadLock();
            try
            {
                return InEdges(commit).Select(e => e.Child).ToList();
            }
            finally
            {
                commitLock.ExitReadLock();
            }
        }
        public bool IsRootCommit(TCommit commit) => GetParents(commit).Any() == false;

        public 

        /// <summary>
        /// Removes a commit, hooking its parents and children together
        /// </summary>
        /// <param name="commit"></param>
        public void RemoveCommit(TCommit commit)
        {
            using (var commitLock = Lock(commit))
            {
                var parents = GetParents(commit);
                var children = GetChildren(commit);

                var newEdges = children.SelectMany(c => parents.Select(p => new Parentage<TCommit>(c, p)));

                AddEdgeRange(newEdges);
                RemoveVertex(commit);
            }
        }
    }

    public sealed class CommitLock : IDisposable
    {
        private ReaderWriterLockSlim[] locks;
        private List<ReaderWriterLockSlim> locked;
        internal CommitLock(ReaderWriterLockSlim[] locks)
        {
            this.locks = locks;
            locked = new List<ReaderWriterLockSlim>(locks.Length);
        }

        public void EnterWriteLock()
        {
            try
            {
                foreach (var l in locks)
                {
                    l.EnterWriteLock();
                    locked.Add(l);
                }
            }
            catch
            {
                ExitWriteLock();
                throw;
            }
        }

        private void ExitWriteLock()
        {
            foreach (var l in locked)
            {
                try
                {
                    l.ExitWriteLock();
                }
                catch { }
            }
            locked.Clear();
        }

        public void Dispose()
        {
            ExitWriteLock();
            foreach (var l in locks)
            {
                try
                {
                    l.ExitUpgradeableReadLock();
                }
                catch { }
            }
        }
    }
}
