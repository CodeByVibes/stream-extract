using System.Collections.ObjectModel;
using StreamExtract.Models;

namespace StreamExtract.Desktop.ViewModels;

public enum TreeSelectionKind
{
    Attachments,
    Chapters,
    Tags,
    CueSheet,
    CuesForSelectedTracks,
    Timestamps
}

public class FileNodeViewModel : ViewModelBase
{
    private bool? _isChecked = false;
    private bool _isExpanded = true;
    private bool _isUpdatingCheck;

    public string Title { get; }
    public string IconKey { get; }
    public object? Tag { get; }
    public FileNodeViewModel? Parent { get; private set; }
    public ObservableCollection<FileNodeViewModel> Children { get; } = [];

    public FileNodeViewModel(string title, string iconKey, object? tag = null)
    {
        Title = title;
        IconKey = iconKey;
        Tag = tag;
    }

    public bool? IsChecked
    {
        get => _isChecked;
        set => SetIsChecked(value, updateChildren: true, updateParent: true);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public void AddChild(FileNodeViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    private void SetIsChecked(bool? value, bool updateChildren, bool updateParent)
    {
        if (_isChecked == value) return;

        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));

        if (updateChildren && value.HasValue)
        {
            _isUpdatingCheck = true;
            foreach (var child in Children)
            {
                child.SetIsChecked(value.Value, updateChildren: true, updateParent: false);
            }
            _isUpdatingCheck = false;
        }

        if (updateParent && Parent is not null && !Parent._isUpdatingCheck)
        {
            Parent.UpdateCheckFromChildren();
        }
    }

    private void UpdateCheckFromChildren()
    {
        if (Children.Count == 0) return;

        bool hasChecked = false;
        bool hasUnchecked = false;
        bool hasIndeterminate = false;

        foreach (var child in Children)
        {
            if (child.IsChecked == true) hasChecked = true;
            else if (child.IsChecked == false) hasUnchecked = true;
            else hasIndeterminate = true;
        }

        bool? state;
        if (hasIndeterminate || (hasChecked && hasUnchecked))
            state = null;
        else if (hasChecked)
            state = true;
        else
            state = false;

        SetIsChecked(state, updateChildren: false, updateParent: true);
    }

    public static FileSelection? BuildFileSelection(FileNodeViewModel fileNode)
    {
        var trackIds = new HashSet<int>();
        bool attachments = false, tags = false, cuesheet = false, timestamps = false, cuesForSelectedTracks = false;
        var chapterIds = new HashSet<int>();

        foreach (var child in fileNode.Children)
        {
            if (child.IsChecked != true) continue;

            switch (child.Tag)
            {
                case int trackId:
                    trackIds.Add(trackId);
                    break;
                case TreeSelectionKind.Attachments:
                    attachments = true;
                    break;
                case TreeSelectionKind.Chapters:
                    chapterIds.Add(0);
                    break;
                case TreeSelectionKind.Tags:
                    tags = true;
                    break;
                case TreeSelectionKind.CueSheet:
                    cuesheet = true;
                    break;
                case TreeSelectionKind.Timestamps:
                    timestamps = true;
                    break;
                case TreeSelectionKind.CuesForSelectedTracks:
                    cuesForSelectedTracks = true;
                    break;
            }
        }

        if (trackIds.Count == 0 && !attachments && chapterIds.Count == 0 && !tags && !cuesheet && !timestamps && !cuesForSelectedTracks)
            return null;

        return new FileSelection(trackIds, attachments, chapterIds, tags, cuesheet, timestamps, cuesForSelectedTracks);
    }
}
