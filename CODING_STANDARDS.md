# Urras Coding Standards

These standards keep the code consistent, readable and portable across Windows and Linux. They follow the same rules
as the other TheSCREWEDSoftware projects, adapted to C#.

Pull requests that don't follow these standards will be asked to change before they are merged.

## 1. File format (all files)

| Rule | Value |
|---|---|
| Encoding | UTF-8 (no BOM) |
| Line endings | LF (`\n`) |
| Final newline | Required |
| Trailing whitespace | Not allowed |
| Indentation | 4 spaces; tabs are forbidden |
| Indentation for JSON, YAML, shell scripts, XML, `.csproj` and `.axaml` | 2 spaces |
| Maximum line length | 120 columns |

Exceptions: `.bat` and `.cmd` files use CRLF, because Windows requires it. `.sln` files are written by Visual Studio
with a BOM, CRLF and tabs; leave them that way.

These rules are enforced by `.editorconfig` (formatting in your editor) and `.gitattributes` (line endings in Git).

## 2. C# formatting

### Braces
Opening and closing braces go on their own lines. A single-line body may omit the braces.

```csharp
if (status.HasChanges)
{
    StageAll();
    Refresh();
}
else
    SetStatus("Nothing to commit.");
```

### Namespaces
Use file-scoped namespaces: `namespace Urras.Core.Git;`.

### Switch statements
Always include a `default` case, even if it only contains `break`. Use braces for multi-line cases.

### Types
Write the type instead of `var`: `List<FileChange> changes = new List<FileChange>();`.

## 3. Naming

| What | Style | Example |
|---|---|---|
| Classes, structs, enums, interfaces | UpperCamelCase | `GitRepository`, `ChangeKind` |
| Methods, properties, events | UpperCamelCase | `GetStatusAsync()`, `IsUnborn` |
| Private fields | `_` + lowerCamelCase | `_repository`, `_isBusy` |
| Private static readonly fields | UpperCamelCase | `QuietCommands` |
| Parameters and local variables | lowerCamelCase | `targetFolder`, `result` |
| Constants | UPPER_SNAKE_CASE | `MAX_LOG_CHARACTERS` |
| Async methods | `Async` suffix | `PushAsync()` |
| Files | Match the main type | `GitRepository.cs` |

- No Hungarian notation (`iCount`, `strName`).
- Float literals use the `f` suffix: `0.25f`.

## 4. Constants

- **No magic numbers.** Give every meaningful number a name.
- Use `const`, `static readonly` or `enum`.

## 5. Git commands

- Every Git operation goes through `GitRunner`, never `Process` directly, so every command is logged.
- Pass arguments as a list, never as one string, so paths with spaces work.
- Prefer machine-readable output (`--porcelain=v2 -z`, custom `--format` with separators) over parsing human output.
- Never run a command that can lose data without asking the user first (discard, force delete, reset `--hard`).

## 6. Layers

- `Urras.Core` has no UI code and no UI package references. Everything Git-related is testable there.
- `Urras.App` holds the Avalonia UI and calls `Urras.Core`.

## 7. Portability

The app must build and run on Windows and Linux.

- Use `Path.Combine` and `System.IO` APIs for paths. Never hardcode paths or use `\` as a separator.
- File names are case-sensitive on Linux. Always match the exact case.
- Keep OS-specific code behind `OperatingSystem.IsWindows()` checks, and only where unavoidable.

## 8. Comments

- Comments are one line, and only where the code isn't self-explanatory.
- Explain *why*, not *what*.
- Don't put links in code comments. Put them in the pull request description.
