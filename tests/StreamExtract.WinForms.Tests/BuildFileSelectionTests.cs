using System.Windows.Forms;
using StreamExtract.Models;

namespace StreamExtract.Tests;

/// <summary>
/// Tests for <see cref="Form1.BuildFileSelection"/>, the checkbox-tree → FileSelection
/// mapping that drives what gets extracted per file.
/// </summary>
public class BuildFileSelectionTests
{
    private static TreeNode FileNode() => new();

    private static TreeNode CheckedNode(object tag) => new() { Tag = tag, Checked = true };

    [Fact]
    public void EmptySelection_ReturnsNull()
    {
        var fileNode = FileNode();
        fileNode.Nodes.Add(new TreeNode { Tag = 0, Checked = false });

        Assert.Null(Form1.BuildFileSelection(fileNode));
    }

    [Fact]
    public void TrackSelection_MapsTrackIds()
    {
        var fileNode = FileNode();
        fileNode.Nodes.Add(CheckedNode(0));
        fileNode.Nodes.Add(CheckedNode(2));

        var selection = Form1.BuildFileSelection(fileNode);

        Assert.NotNull(selection);
        Assert.Equal([0, 2], selection!.TrackIds.OrderBy(x => x));
    }

    [Fact]
    public void ChaptersSelection_MapsToChapterIdZero()
    {
        var fileNode = FileNode();
        fileNode.Nodes.Add(CheckedNode(Form1.SelectionKind.Chapters));

        var selection = Form1.BuildFileSelection(fileNode);

        Assert.NotNull(selection);
        Assert.Contains(0, selection!.ChapterIds);
    }

    [Fact]
    public void UncheckedChildren_AreIgnored()
    {
        var fileNode = FileNode();
        fileNode.Nodes.Add(new TreeNode { Tag = 0, Checked = true });
        fileNode.Nodes.Add(new TreeNode { Tag = 1, Checked = false });

        var selection = Form1.BuildFileSelection(fileNode);

        Assert.NotNull(selection);
        Assert.Equal([0], selection!.TrackIds);
    }

    [Fact]
    public void FeatureFlags_AreMapped()
    {
        var fileNode = FileNode();
        fileNode.Nodes.Add(CheckedNode(Form1.SelectionKind.Attachments));
        fileNode.Nodes.Add(CheckedNode(Form1.SelectionKind.Tags));
        fileNode.Nodes.Add(CheckedNode(Form1.SelectionKind.CueSheet));
        fileNode.Nodes.Add(CheckedNode(Form1.SelectionKind.Timestamps));
        fileNode.Nodes.Add(CheckedNode(Form1.SelectionKind.CuesForSelectedTracks));

        var selection = Form1.BuildFileSelection(fileNode);

        Assert.NotNull(selection);
        Assert.True(selection!.ExtractAttachments);
        Assert.True(selection.ExtractTags);
        Assert.True(selection.ExtractCueSheets);
        Assert.True(selection.ExtractTimestamps);
        Assert.True(selection.ExtractCuesForSelectedTracks);
    }

    [Fact]
    public void CuesForSelectedTracksOnly_IsNotEmptySelection()
    {
        var fileNode = FileNode();
        fileNode.Nodes.Add(CheckedNode(Form1.SelectionKind.CuesForSelectedTracks));

        var selection = Form1.BuildFileSelection(fileNode);

        Assert.NotNull(selection);
        Assert.True(selection!.ExtractCuesForSelectedTracks);
    }
}
