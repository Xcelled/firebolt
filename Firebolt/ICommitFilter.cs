using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt
{
    interface ICommitLimiter
    {
        IEnumerable<Commit> Limit(IEnumerable<Commit> current);
    }

    interface IFilter
    {
        IEnumerable<CommitMetadata> Run(CommitMetadata current, Commit original, IRepository repo);
    }

    class TreeMetadata : TreeDefinition
    {
        private static readonly FieldInfo baseEntriesField = typeof(TreeDefinition)
                .GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance);

        public IEnumerable<string> EntryNames => entries.Keys;

        private readonly Dictionary<string, TreeEntryDefinition> entries;
        public TreeMetadata()
        {
            entries = baseEntriesField.GetValue(this) as Dictionary<string, TreeEntryDefinition>;
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

    class CommitMetadata
    {
        public Signature Author { get; }
        public Signature Committer { get; }
        public string Message { get; }
        public TreeMetadata Tree { get; }
        public IEnumerable<Commit> Parents { get; }

        public CommitMetadata(Signature author, Signature comitter, string message, TreeMetadata tree, IEnumerable<Commit> parents)
        {
            this.Author = author;
            this.Committer = comitter;
            this.Message = message;
            this.Tree = tree;
            this.Parents = parents;
        }

        public static CommitMetadata From(Commit commit, Signature author = null, Signature comitter = null, string message = null, TreeMetadata tree = null, IEnumerable<Commit> parents = null)
        {
            return new CommitMetadata(author ?? commit.Author,
                comitter ?? commit.Committer,
                message ?? commit.Message,
                tree ?? TreeMetadata.From(commit.Tree),
                parents ?? commit.Parents);
        }

        public static CommitMetadata From(CommitMetadata commit, Signature author = null, Signature comitter = null, string message = null, TreeMetadata tree = null, IEnumerable<Commit> parents = null)
        {
            return new CommitMetadata(author ?? commit.Author,
                comitter ?? commit.Committer,
                message ?? commit.Message,
                tree ?? commit.Tree,
                parents ?? commit.Parents);
        }
    }
}
