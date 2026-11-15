using System.Windows;
using YrClean.Core.Models;
using YrClean.Core.Services;
using MessageBox = System.Windows.MessageBox;

namespace YrClean.UI;

// The operation journal, newest first. Operations whose files are still in quarantine
// expand to those files and can be rolled back, fully or file by file.
public partial class QuarantineWindow : Window
{
    private List<OperationNode> _operations = new();

    public QuarantineWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Reload();
    }

    private void Reload()
    {
        var settings = SettingsService.Load();
        var batches = QuarantineService.Default.ListBatches();
        var journal = OperationJournal.Read();
        var journaledBatchIds = journal.Select(e => e.BatchId).ToHashSet();

        // A batch missing from the journal (e.g. the journal file was removed) must stay restorable
        var orphans = batches
            .Where(b => !journaledBatchIds.Contains(b.Id))
            .Select(b => new JournalEntry(b.CreatedUtc.ToLocalTime(), "cleanup", b.Entries.Count, b.TotalBytes, 0, 0, b.Id));

        _operations = journal.Concat(orphans)
            .OrderByDescending(e => e.Time)
            .Take(300)
            .Select(e => new OperationNode(e, batches.FirstOrDefault(b => b.Id == e.BatchId)))
            .ToList();
        OperationsTree.ItemsSource = _operations;

        SummaryText.Text =
            $"In quarantine: {batches.Sum(b => b.Entries.Count)} files, {SizeFormatter.Format(batches.Sum(b => b.TotalBytes))}. " +
            $"Files are deleted permanently {settings.QuarantineDays} days after they were cleaned.";
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var restored = 0;
        var errors = new List<string>();

        foreach (var operation in _operations.Where(o => o.CanRestore))
        {
            var storedNames = operation.Children.Where(f => f.IsSelected).Select(f => f.StoredName).ToList();
            if (storedNames.Count == 0)
                continue;

            var result = QuarantineService.Default.Restore(operation.BatchId!, storedNames);
            restored += result.RestoredCount;
            errors.AddRange(result.Errors);
            OperationJournal.Append(new JournalEntry(
                DateTime.Now, "restore", result.RestoredCount, 0, 0, result.Errors.Count,
                Note: $"from {operation.BatchId}"));
        }

        if (restored == 0 && errors.Count == 0)
        {
            MessageBox.Show(this, "No files selected.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = $"Restored: {restored} files.";
        if (errors.Count > 0)
            message += $"\n\nNot restored: {errors.Count}\n" + string.Join("\n", errors.Take(10));

        MessageBox.Show(this, message, Title, MessageBoxButton.OK,
            errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        Reload();
    }
}

public class OperationNode : SelectableNodeBase
{
    public OperationNode(JournalEntry entry, QuarantineBatchInfo? batch)
        : base($"{entry.Time:yyyy-MM-dd HH:mm}  {entry.Kind}  -  {entry.Items} files" +
               (entry.Note is null ? "" : $"  ({entry.Note})"),
               batch is null ? "" : $"{SizeFormatter.Format(batch.TotalBytes)} restorable")
    {
        BatchId = batch?.Id;
        Children = (batch?.Entries ?? new List<QuarantineEntry>())
            .Select(e => new QuarantinedFileNode(e))
            .ToList();
    }

    public string? BatchId { get; }
    public bool CanRestore => BatchId is not null;
    public List<QuarantinedFileNode> Children { get; }

    protected override IEnumerable<SelectableNodeBase> GetChildren() => Children;
}

public class QuarantinedFileNode : SelectableNodeBase
{
    public QuarantinedFileNode(QuarantineEntry entry)
        : base(entry.OriginalPath, SizeFormatter.Format(entry.SizeBytes))
    {
        StoredName = entry.StoredName;
    }

    public string StoredName { get; }

    protected override IEnumerable<SelectableNodeBase> GetChildren() => Array.Empty<SelectableNodeBase>();
}
