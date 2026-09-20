using System.Windows.Forms;
using StreamExtract.Models;
using StreamExtract.Plugins;
using StreamExtract.Services;

namespace StreamExtract.Tests;

/// <summary>
/// Tests for <see cref="Form1.RemoveFileNodeFromList"/>, the helper behind the tree's
/// Delete key: it detaches a media file from the list without touching the file on disk.
/// </summary>
public class RemoveFileNodeFromListTests
{
    private sealed class StubToolResolver : INativeToolResolver
    {
        public string Resolve(NativeToolId id) => string.Empty;
    }

    private static ImportedFile Imported(string path)
    {
        var info = new MediaFileInfo(
            path, Path.GetFileName(path), ExtractorFeatures.Tracks | ExtractorFeatures.Chapters,
            [new TrackInfo(0, TrackType.Video, "h264", "Video", "eng", new())],
            [new ChapterInfo(0, "Chapter 1", "eng")],
            [], []);
        return new ImportedFile(path, new MkvExtractorPlugin(new StubToolResolver()), info);
    }

    /// <summary>
    /// A bare <see cref="TreeNode"/> exposes the same <see cref="TreeNodeCollection"/>
    /// shape as <c>TreeView.Nodes</c>, so the helper can be exercised without a control.
    /// </summary>
    private static (TreeNode Root, List<ImportedFile> Files) Setup()
    {
        var root = new TreeNode();
        var files = new List<ImportedFile>();

        foreach (var path in new[] { "/media/a.mkv", "/media/b.mkv" })
        {
            var imported = Imported(path);
            files.Add(imported);
            root.Nodes.Add(new TreeNode(Path.GetFileName(path)) { Tag = imported });
        }

        return (root, files);
    }

    [Fact]
    public void RootNode_IsRemovedFromTreeAndFileList()
    {
        var (root, files) = Setup();
        var target = root.Nodes[0];
        var imported = (ImportedFile)target.Tag!;

        var removed = Form1.RemoveFileNodeFromList(root.Nodes, files, target);

        Assert.Equal(1, removed);
        Assert.Single(root.Nodes);
        Assert.DoesNotContain(imported, files);
    }

    [Fact]
    public void ChildNode_IsIgnored()
    {
        var (root, files) = Setup();
        var trackNode = new TreeNode("Track1") { Tag = 0 };
        root.Nodes[0].Nodes.Add(trackNode);

        var removed = Form1.RemoveFileNodeFromList(root.Nodes, files, trackNode);

        Assert.Equal(0, removed);
        Assert.Equal(2, root.Nodes.Count);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void SelectionFromAnotherTree_IsIgnored()
    {
        var (root, files) = Setup();
        var foreign = new TreeNode("c.mkv") { Tag = Imported("/media/c.mkv") };

        var removed = Form1.RemoveFileNodeFromList(root.Nodes, files, foreign);

        Assert.Equal(0, removed);
        Assert.Equal(2, root.Nodes.Count);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void NullSelection_IsIgnored()
    {
        var (root, files) = Setup();

        Assert.Equal(0, Form1.RemoveFileNodeFromList(root.Nodes, files, null));
        Assert.Equal(2, root.Nodes.Count);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void AlreadyRemovedNode_IsANoOp()
    {
        var (root, files) = Setup();
        var target = root.Nodes[0];
        Form1.RemoveFileNodeFromList(root.Nodes, files, target);

        var removed = Form1.RemoveFileNodeFromList(root.Nodes, files, target);

        Assert.Equal(0, removed);
        Assert.Single(root.Nodes);
        Assert.Single(files);
    }

    [Fact]
    public void RemovingAnEntry_LeavesTheFileOnDiskUntouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"streamextract-{Guid.NewGuid():N}.mkv");
        File.WriteAllText(path, "stand-in for a media file");
        try
        {
            var root = new TreeNode();
            var files = new List<ImportedFile> { Imported(path) };
            root.Nodes.Add(new TreeNode(Path.GetFileName(path)) { Tag = files[0] });

            var removed = Form1.RemoveFileNodeFromList(root.Nodes, files, root.Nodes[0]);

            Assert.Equal(1, removed);
            Assert.Empty(files);
            Assert.Empty(root.Nodes.Cast<TreeNode>());
            Assert.True(File.Exists(path), "removing a list entry must not delete the file on disk");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
