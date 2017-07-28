using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt.Core
{
    public class TreeMetadata : TreeDefinition, IEquatable<TreeMetadata>, IEquatable<TreeDefinition>, IEquatable<Tree>
    {
        private static readonly FieldInfo baseEntriesField = typeof(TreeDefinition)
                .GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);

        public IEnumerable<string> EntryNames => entries.Keys;

        public bool IsEmpty => entries.Count == 0;

        private readonly Dictionary<string, TreeEntryDefinition> entries;
        public TreeMetadata()
        {
            entries = baseEntriesField.GetValue(this) as Dictionary<string, TreeEntryDefinition>;
        }

        public bool Equals(TreeDefinition other)
        {
            var otherEntries = baseEntriesField.GetValue(other) as Dictionary<string, TreeEntryDefinition>;
            return entries.DictionaryEqual(otherEntries);
        }

        public bool Equals(TreeMetadata other)
        {
            return ReferenceEquals(this, other) || entries.DictionaryEqual(other.entries);
        }

        public bool Equals(Tree other)
        {
            return Equals(TreeDefinition.From(other));
        }

        public static new TreeMetadata From(Tree tree)
        {
            var newTree = new TreeMetadata();

            foreach (var te in tree)
            {
                newTree.Add(te.Name, te);
            }

            return newTree;
        }

        public static TreeMetadata From(TreeDefinition tree)
        {
            var newTree = new TreeMetadata();
            var treeEntries = baseEntriesField.GetValue(tree) as Dictionary<string, TreeEntryDefinition>;

            foreach (var name in treeEntries.Keys)
            {
                newTree.Add(name, tree[name]);
            }

            return newTree;
        }

        public static TreeMetadata From(TreeMetadata tree)
        {
            var newTree = new TreeMetadata();

            foreach (var name in tree.entries.Keys)
            {
                newTree.Add(name, tree[name]);
            }

            return newTree;
        }
    }

    public class CommitMetadata : IEquatable<CommitMetadata>
    {
        public virtual Signature Author { get; set; }
        public virtual Signature Committer { get; set; }
        public virtual string Message { get; set; }
        public virtual TreeMetadata Tree { get; set; }
        public Commit Original { get; }
        public virtual bool IsBoundary => false;

        public CommitMetadata (Commit commit, Signature author = null, Signature committer = null, string message = null, TreeMetadata tree = null)
        {
            Author = author ?? commit.Author;
            Committer = committer ?? commit.Committer;
            Message = message ?? commit.Message;
            Tree = tree ?? TreeMetadata.From(commit.Tree);
            Original = commit;
        }

        public CommitMetadata (CommitMetadata commit, Signature author = null, Signature committer = null, string message = null, TreeMetadata tree = null)
        {
            Author = author ?? commit.Author;
            Committer = committer ?? commit.Committer;
            Message = message ?? commit.Message;
            Tree = tree ?? commit.Tree;
            Original = commit.Original;
        }

        public bool Equals(CommitMetadata other)
        {
            return this.Author.Equals(other.Author) &&
                this.Committer.Equals(other.Committer) &&
                this.Message.Equals(other.Message, StringComparison.Ordinal) &&
                this.Tree.Equals(other.Tree);
        }
    }

    /// <summary>
    /// Represents a boundary commit
    /// </summary>
    public class BoundaryCommit : CommitMetadata, IEquatable<Commit>
    {
        public override Signature Author { get => base.Author; set => throw new InvalidOperationException(); }
        public override Signature Committer { get => base.Committer; set => throw new InvalidOperationException(); }
        public override string Message { get => base.Message; set => throw new InvalidOperationException(); }
        public override TreeMetadata Tree { get => base.Tree; set => throw new InvalidOperationException(); }
        public override bool IsBoundary => true;

        public BoundaryCommit(Commit commit) : base(commit)
        { }

        public override int GetHashCode()
        {
            return Original.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return Original.Equals(obj);
        }

        public bool Equals(Commit other)
        {
            return Original.Equals(other);
        }
    }
}