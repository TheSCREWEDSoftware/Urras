using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Urras.Core.Git;
using Urras.Core.Settings;

namespace Urras.App;

// A group or repository in the repository tree
public sealed class RepositoryNode : INotifyPropertyChanged
{
    private const double MISSING_OPACITY = 0.55;

    private static IBrush MutedBrush => ThemeBrushes.MutedText;
    private static IBrush AttentionBrush => ThemeBrushes.Attention;
    private static IBrush LocalBrush => ThemeBrushes.Warning;
    private static IBrush MissingBrush => ThemeBrushes.Error;

    private readonly Action<RepositoryNode> _expandedChanged;
    private bool _isExpanded;
    private string _detail;
    private IBrush _detailBrush = MutedBrush;
    private double _itemOpacity = 1.0;

    public RepositoryNode(RepositoryTreeNode source, RepositoryNode? parent, bool isExpanded,
                          Action<RepositoryNode> expandedChanged)
    {
        Source = source;
        Parent = parent;
        _isExpanded = isExpanded;
        _expandedChanged = expandedChanged;
        _detail = source.IsGroup ? string.Empty : "…";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RepositoryTreeNode Source { get; }
    public RepositoryNode? Parent { get; }
    public ObservableCollection<RepositoryNode> Children { get; } = new ObservableCollection<RepositoryNode>();

    public string Name => Source.IsPinned ? "[P] " + Source.Name : Source.Name;
    public string? Path => Source.RepositoryPath;
    public bool IsGroup => Source.IsGroup;
    public FontWeight NameWeight => IsGroup ? FontWeight.SemiBold : FontWeight.Normal;

    // The last of its parent's children, whose guide line ends at its name (see TreeGuides)
    public bool IsLastChild => Parent == null || Parent.Children.Count == 0 || Parent.Children[^1] == this;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            OnPropertyChanged();
            _expandedChanged(this);
        }
    }

    public string Detail
    {
        get => _detail;
        private set
        {
            _detail = value;
            OnPropertyChanged();
        }
    }

    public IBrush DetailBrush
    {
        get => _detailBrush;
        private set
        {
            _detailBrush = value;
            OnPropertyChanged();
        }
    }

    public double ItemOpacity
    {
        get => _itemOpacity;
        private set
        {
            _itemOpacity = value;
            OnPropertyChanged();
        }
    }

    // "main · origin ↑1 ↓2 · ●3", "main · local only", "missing: folder not found"
    public void ApplySummary(RepositorySummary summary)
    {
        if (summary.State == RepositoryState.Missing)
        {
            Detail = "missing: folder not found";
            DetailBrush = MissingBrush;
            ItemOpacity = MISSING_OPACITY;
            return;
        }

        ItemOpacity = 1.0;
        string branch = summary.IsDetached ? "detached" : summary.Branch ?? "?";
        string text;
        IBrush brush = MutedBrush;

        switch (summary.State)
        {
            case RepositoryState.LocalOnly:
            {
                text = branch + " · local only";
                brush = LocalBrush;
                break;
            }
            case RepositoryState.NotPublished:
            {
                text = branch + " · not published";
                brush = LocalBrush;
                break;
            }
            default:
            {
                text = branch + " · " + summary.UpstreamRemote;
                if (summary.Ahead > 0)
                    text += " ↑" + summary.Ahead;
                if (summary.Behind > 0)
                    text += " ↓" + summary.Behind;
                if (summary.Ahead > 0 || summary.Behind > 0)
                    brush = AttentionBrush;
                break;
            }
        }

        if (summary.Changes > 0)
        {
            text += " · ●" + summary.Changes;
            brush = AttentionBrush;
        }

        Detail = text;
        DetailBrush = brush;
    }

    // "4 repositories · 2 need attention"
    public void UpdateGroupDetail(IReadOnlyDictionary<string, RepositorySummary> summaries)
    {
        if (!IsGroup)
            return;

        List<string> repositories = RepositoryLayout.AllRepositories(Source).ToList();
        int attention = repositories.Count(path =>
            summaries.TryGetValue(RepositoryLayout.Normalize(path), out RepositorySummary? summary) &&
            NeedsAttention(summary));

        Detail = repositories.Count + (repositories.Count == 1 ? " repository" : " repositories") +
                 (attention > 0 ? " · " + attention + " need attention" : string.Empty);
        DetailBrush = attention > 0 ? AttentionBrush : MutedBrush;
    }

    public IEnumerable<RepositoryNode> SelfAndDescendants()
    {
        yield return this;
        foreach (RepositoryNode child in Children)
        {
            foreach (RepositoryNode node in child.SelfAndDescendants())
                yield return node;
        }
    }

    // "Echo Flint Engine / 0_Engine"
    public string FullName => Parent == null ? Name : Parent.FullName + " / " + Name;

    private static bool NeedsAttention(RepositorySummary summary)
    {
        return summary.State != RepositoryState.Published || summary.Changes > 0 || summary.Ahead > 0 ||
               summary.Behind > 0;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
