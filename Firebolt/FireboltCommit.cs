using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Firebolt
{
    public class TreeMetadata : TreeDefinition
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

    public class FireboltCommit
    {
        public Signature Author { get; }
        public Signature Committer { get; }
        public string Message { get; }
        public TreeMetadata Tree { get; }
        public HashSet<Commit> Parents { get; }

        public FireboltCommit(Signature author, Signature committer, string message, TreeMetadata tree, IEnumerable<Commit> parents)
        {
            this.Author = author;
            this.Committer = committer;
            this.Message = message;
            this.Tree = tree;
            this.Parents = parents.ToSet();
        }

        public static FireboltCommit From(Commit commit, Signature author = null, Signature committer = null, string message = null, TreeMetadata tree = null, IEnumerable<Commit> parents = null)
        {
            return new FireboltCommit(author ?? commit.Author,
                committer ?? commit.Committer,
                message ?? commit.Message,
                tree ?? TreeMetadata.From(commit.Tree),
                parents ?? commit.Parents);
        }

        public static FireboltCommit From(FireboltCommit commit, Signature author = null, Signature committer = null, string message = null, TreeMetadata tree = null, IEnumerable<Commit> parents = null)
        {
            return new FireboltCommit(author ?? commit.Author,
                committer ?? commit.Committer,
                message ?? commit.Message,
                tree ?? commit.Tree,
                parents ?? commit.Parents);
        }
    }
}