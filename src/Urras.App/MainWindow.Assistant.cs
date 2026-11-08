using Urras.Core.Assistant;
using Urras.Core.Git;

namespace Urras.App;

// The AI assistant: commit messages from the staged changes, explaining commits, and conflict suggestions
public partial class MainWindow
{
    private static readonly HttpClient AssistantHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

    private void InitializeAssistant()
    {
        WriteMessageButton.Click += async (_, _) => await WriteCommitMessageAsync();
        UpdateAssistantButtons();
    }

    private void UpdateAssistantButtons()
    {
        WriteMessageButton.IsVisible = _settings.Assistant.IsOn;
    }

    private async Task<AiAssistant?> CreateAssistantAsync()
    {
        if (!_settings.Assistant.IsOn)
        {
            SetStatus("Turn on the AI assistant in Options first.", true);
            return null;
        }

        string? key = _accounts == null ? null : await _accounts.LoadSecretAsync(AssistantSettings.KEY_NAME);
        return new AiAssistant(_settings.Assistant, key, AssistantHttp);
    }

    private async Task WriteCommitMessageAsync()
    {
        if (_repository == null || _status == null || await CreateAssistantAsync() is not { } assistant)
            return;

        if (_status.Staged.Count == 0)
        {
            SetStatus("Stage the changes the message should describe first.", true);
            return;
        }

        if ((!string.IsNullOrWhiteSpace(SummaryBox.Text) || !string.IsNullOrWhiteSpace(DescriptionBox.Text)) &&
            !await Dialogs.ConfirmAsync(this, "Write message", "Replace the message you've written?", "Replace"))
            return;

        WriteMessageButton.IsEnabled = false;
        SetStatus("Asking the AI assistant for a message…", false);
        try
        {
            string diff = await _repository.GetCombinedDiffAsync(_status.Staged, true);
            string? template = await _repository.GetCommitTemplateAsync();
            string answer = AssistantPrompts.Unfence(await assistant.AskAsync(
                AssistantPrompts.CommitMessageInstructions(template),
                AssistantPrompts.CommitMessageInput(diff, _history.Select(commit => commit.Subject))));

            string[] parts = answer.Replace("\r\n", "\n").Split('\n', 2);
            SummaryBox.Text = parts[0].Trim();
            DescriptionBox.Text = parts.Length > 1 ? parts[1].Trim('\n') : string.Empty;
            SetStatus("Message written; check it before committing.", false);
        }
        catch (AssistantException exception)
        {
            SetStatus(exception.Message, true);
        }
        finally
        {
            WriteMessageButton.IsEnabled = true;
        }
    }

    private async Task ExplainCommitAsync(CommitInfo commit)
    {
        if (_repository == null || await CreateAssistantAsync() is not { } assistant)
            return;

        SetStatus("Asking the AI assistant about " + commit.ShortHash + "…", false);
        try
        {
            string diff = await _repository.GetCommitDiffAsync(commit.Hash);
            string answer = await assistant.AskAsync(AssistantPrompts.EXPLAIN_INSTRUCTIONS,
                                                     "Commit " + commit.ShortHash + ": " + commit.Subject + "\n\n" +
                                                     diff);
            SetStatus("Ready", false);
            await Dialogs.ShowTextAsync(this, "About " + commit.ShortHash, answer);
        }
        catch (AssistantException exception)
        {
            SetStatus(exception.Message, true);
        }
    }

    // A few lines either side give the assistant enough context without sending the whole file
    private async Task<string?> SuggestConflictAsync(string path, ConflictFile file, ConflictBlock block)
    {
        if (await CreateAssistantAsync() is not { } assistant)
            return null;

        const int CONTEXT_LINES = 10;
        int index = file.Blocks.IndexOf(block);
        IEnumerable<string> before = index > 0 ? (file.Blocks[index - 1].Common ?? new List<string>())
                                                 .TakeLast(CONTEXT_LINES) : Array.Empty<string>();
        IEnumerable<string> after = index + 1 < file.Blocks.Count
            ? (file.Blocks[index + 1].Common ?? new List<string>()).Take(CONTEXT_LINES)
            : Array.Empty<string>();
        try
        {
            string answer = await assistant.AskAsync(AssistantPrompts.CONFLICT_INSTRUCTIONS,
                                                     AssistantPrompts.ConflictInput(path, block, before, after));
            return AssistantPrompts.Unfence(answer);
        }
        catch (AssistantException exception)
        {
            SetStatus(exception.Message, true);
            return null;
        }
    }
}
