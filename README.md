# Urras

A fast, native Git client for Windows and Linux. No Electron: C# on .NET with an Avalonia interface, running the `git`
you already have installed.

**Status:** early development. First version with the basic Git workflow.

## Features

- Repository list: add existing folders, create new repositories, clone
- Changes: unstaged and staged files, stage and unstage (selected or all), discard with confirmation
- Diffs for modified, new, deleted and renamed files
- Commit with summary and description, amend the last commit (Ctrl+Enter to commit)
- History with the full diff of each commit
- Branches: switch, create, delete, check out remote branches
- Fetch, pull and push, with ahead and behind counts; first push publishes the branch
- Git log panel showing every command Urras runs and its output

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- Git 2.25 or newer: on Windows, [Git for Windows](https://git-scm.com/download/win), which includes Git Credential
  Manager for signing in to GitHub; on Linux, `git` from your package manager plus `git-credential-manager` or
  `git-credential-libsecret`

## Building and running

```
dotnet run --project src/Urras.App
```

Or open `Urras.sln` in Visual Studio or Rider and run `Urras.App`.

To build a standalone app:

```
dotnet publish src/Urras.App -c Release -r win-x64 --self-contained
dotnet publish src/Urras.App -c Release -r linux-x64 --self-contained
```

### Tests

The Git layer (`Urras.Core`) has tests that run against real temporary repositories:

```
dotnet run --project tests/Urras.Core.Tests
```

## Structure

```
src/Urras.Core/          Git commands, output parsers, settings. No UI.
src/Urras.App/           Avalonia user interface.
tests/Urras.Core.Tests/  Tests for Urras.Core, with a small built-in runner.
```

Settings (the repository list) are stored in `%APPDATA%\Urras\settings.json` on Windows and
`~/.config/Urras/settings.json` on Linux.

## Contributing

Read [CODING_STANDARDS.md](CODING_STANDARDS.md) before opening a pull request. Commit messages follow
[.git_commit_template.txt](.git_commit_template.txt). AI-assisted pull requests are allowed, but must be disclosed in
the pull request template.

## Credits

- [Avalonia](https://github.com/AvaloniaUI/Avalonia) (MIT) for the user interface.

## License

The source code is **source-available, not open source**. You may read it, build it and use Urras for your own work,
and contribute, but you may not redistribute it or use it commercially. See [LICENSE.md](LICENSE.md) for the full
terms.
