# Urras

<img src="src/Urras.App/Assets/urras.png" alt="Urras icon: shards of ice in a blizzard" width="96">

A fast, native Git client for Windows and Linux. No Electron: C# on .NET with an Avalonia interface, running the `git`
you already have installed.

**Status:** early development. First version with the basic Git workflow.

![Urras: grouped repositories with search, the history graph and a diff](docs/screenshots/main-window.png)

## Contents

- [Run it yourself](#run-it-yourself)
  - [What you need](#what-you-need)
  - [Get the code](#get-the-code)
  - [Run it](#run-it)
  - [Build a standalone app](#build-a-standalone-app)
  - [Run the tests](#run-the-tests)
- [Features](#features)
- [Screenshots](#screenshots)
- [For maintainers: build types and releases](#for-maintainers-build-types-and-releases)
- [Project layout and settings](#project-layout-and-settings)
- [Contributing](#contributing)
- [Credits](#credits)
- [License](#license)

The ▸ sections below open and close when you click their titles.

## Run it yourself

### What you need

| You need | Windows | Linux |
|---|---|---|
| .NET 10 SDK, to build | [Download](https://dotnet.microsoft.com/download) | Same download, or your package manager |
| Git 2.25 or newer | [Git for Windows](https://git-scm.com/download/win) | `git` from your package manager |
| Signing in to sites | Comes with Git for Windows | `git-credential-manager` or `git-credential-libsecret` |

Check that both are found by opening a terminal and running `dotnet --version` (10.0 or newer) and `git --version`.

### Get the code

```
git clone https://github.com/TheSCREWEDSoftware/Urras.git
cd Urras
```

### Run it

**Windows, the easy way:** double-click `start.bat`. It builds Urras and opens it in its own window; if Urras is
already open it asks to close it first (Enter means yes), and it tells you if Urras closes right after starting.

**Any system, from a terminal** in the Urras folder:

```
dotnet run --project src/Urras.App
```

**From an IDE:** open `Urras.sln` in Visual Studio or Rider and run `Urras.App`.

The first build downloads Avalonia and the other packages from NuGet, so it needs an internet connection and takes a
minute; later builds are quick.

### Build a standalone app

To make a folder you can copy to another computer that doesn't have .NET installed:

```
dotnet publish src/Urras.App -c Release -r win-x64 --self-contained
dotnet publish src/Urras.App -c Release -r linux-x64 --self-contained
```

The app ends up in `src/Urras.App/bin/Release/net10.0/<win-x64 or linux-x64>/publish/`.

### Run the tests

**Windows:** double-click `test.bat` to run every test and the coding standards check. From a command prompt in the
Urras folder, `test core` or `test ui` runs just one of them.

**Any system:**

```
dotnet run --project tests/Urras.Core.Tests
dotnet test tests/Urras.App.Tests
```

<details>
<summary><b>More about the tests</b></summary>

The Git layer (`Urras.Core`) has tests that run against real temporary repositories. Each line shows its place in
the run, PASS in green or FAIL in red, and a failure stays on one line with line endings written out (`\r\n`):

<img src="docs/screenshots/tests-core.png" alt="Screenshot: the core tests running"><br>
<img src="docs/screenshots/tests-core-summary.png" alt="Screenshot: the core tests' summary">

The interface tests open every window and dialog without showing them (Avalonia headless) and fail on any error:

<img src="docs/screenshots/tests-interface.png" alt="Screenshot: the interface tests passing">

Both use their own settings and Git configuration in a temporary folder, so they never touch yours.

</details>

## Features

<details>
<summary><b>Repositories</b></summary>

- Repository list: add existing folders, create new repositories, clone
- Repository groups: by name (each repository with its forks and clones inside), by folder, by owner, or a
  manual layout you can rename and reorder; pin repositories and folders to the top (marked [P]) and to the
  toolbar; fetch or pull a whole group; each repository shows its branch, remote, commits to push and pull,
  and uncommitted changes
- Import many repositories at once: everything in a folder, GitHub Desktop's list, or cloned from GitHub
- Open repositories and files in the file explorer, a terminal or VS Code; copy paths and commit SHAs
- Quick Launch (Ctrl+P): open a repository, switch branch or run a command by typing a few letters
- Search the repository list (Ctrl+F) by name, folder or group; Enter opens the first one found
- Refreshes when something changes on disk, not every time the window comes back, so it stays quick with many
  repositories
- File menu like GitHub Desktop's: New repository (with a README, a .gitignore template and a licence), Add
  local repository and Clone (owner/name is enough for GitHub), with the same shortcuts
- A screen for repositories whose folder is gone (Locate, Clone again or Remove), and a worktree switcher

</details>

<details>
<summary><b>Changes and commits</b></summary>

- Changes: unstaged and staged files, stage and unstage (selected or all), discard with confirmation
- Co-authors on commits (Co-authored-by lines), picked from people in the history, typed as `Name <email>`, or
  found as @username with a GitHub account
- Stage, unstage or discard single lines or whole hunks: select lines in the diff and right-click
- Commit with summary and description, amend the last commit (Ctrl+Enter to commit)
- Filter the change list, hide untracked files, and see line-ending problems (checked against `.gitattributes`
  and `.editorconfig`) before they are committed
- Commit message helpers: summary length counter, recent messages, and the repository's commit template
- Uncommitted changes can stay with their branch: put away when you switch, back when you return

</details>

<details>
<summary><b>Diffs</b></summary>

- Diffs for modified, new, deleted and renamed files, as unified (with line numbers), split (side by side) or raw
  `git` output; the choice is saved
- Markdown files rendered, old and new side by side with changed blocks marked (Preview switch)
- Syntax colours for common languages, word-level highlights in changed lines, optional whitespace and
  CRLF/LF markers, wrapped or scrolling lines
- Commits shown per file (a list of changed files) or combined; images compared side by side, as a swipe or
  as an onion skin

</details>

<details>
<summary><b>History and the past</b></summary>

- History with the full diff of each commit, a commit graph with branch lanes across every branch, and badges
  for branches, remote branches and tags
- Looking into the past: history of a file or folder, blame, browsing the files at any commit, searching commits
  by message, author, file or content, comparing two commits (Ctrl+click) or a branch, and the reflog for
  finding lost commits
- Statistics: contributors, when commits happen and which files change most

</details>

<details>
<summary><b>Branches, syncing and publishing</b></summary>

- Branches: switch, create, delete, check out remote branches
- Fetch, pull and push from one button, like GitHub Desktop's: an arrow up or down, the ahead and behind counts,
  and when it last fetched; the first push publishes the branch
- Publish repository: a repository with no remote is made on GitHub, GitLab or Gitea (under your account or an
  organization, private or public), added as origin and pushed
- Force push after a rewrite: when a pushed branch's commits were rewritten (authors fixed, amended, rebased), Sync
  offers to replace the remote's old copies instead of pulling them back in; force pushes use --force-with-lease
- Background fetching of every repository (Settings)
- Close button can minimise Urras to the system tray instead of exiting, and Urras can ask before exiting (Settings)
- Tags, checking out a commit, revert, cherry-pick, stashes, merge and rebase
- Bisect from History with a banner to mark commits; submodules, worktrees and Git LFS (with locks) from
  the Branch menu
- Interactive rebase from History: reorder, reword, edit, squash, fix up or drop commits, with a backup tag
- Conflict helper: a banner while a merge, rebase, cherry-pick or revert is stopped, taking one side or marking
  files resolved, then Continue or Abort
- Conflict view: both sides of every conflict next to each other, a choice per conflict and the result below
- Repository settings: line-ending rules written to `.gitattributes` and `.editorconfig`, and normalizing
  existing files after previewing them

</details>

<details>
<summary><b>Accounts and hosting sites</b></summary>

- Accounts on GitHub, GitHub Enterprise, GitLab, Gitea (Forgejo, Codeberg), Bitbucket and Bitbucket Server,
  with tokens in the system's credential store: profile pictures, pull requests (list, check out, create),
  notifications, and cloning your repositories
- Sign in to GitHub through the browser, like GitHub Desktop; GitLab and GitHub Enterprise too once registered
  ([docs/oauth.md](docs/oauth.md)), and a personal access token for everything else
- View a repository on GitHub, GitLab, Bitbucket or Codeberg; forks ask once, like GitHub Desktop, whether they
  contribute to the parent or are for your own purposes, and say what you can push to

</details>

<details>
<summary><b>Look, settings and tools</b></summary>

- Settings backed up every day for a week (`%APPDATA%\Urras\backups`), and unexpected errors written to
  `crash.log` instead of closing Urras
- Custom commands: run builds, scripts or other tools from the menus, with the repository, branch, file or
  commit filled in
- Options: accounts, external editor and shell (or your own), Git name, email and default branch, theme, date
  and time formats, tab size, which questions to ask, what happens to changes when switching branch, background
  fetch
- AI assistant with the service of your choice (OpenAI-compatible, Anthropic or a command-line agent): commit
  messages from the staged changes, explaining commits, conflict suggestions; off by default
- Git log panel showing every command Urras runs and its output
- Failed commands open a window with Git's error, like GitHub Desktop (can be turned off)
- Commit signing with an SSH or GPG key (Options > Git), for GitHub's Verified badge
- A daily check for a new Urras on GitHub, offered from the title bar; Help > Check for updates asks at once
- Keyboard shortcuts listed under Help (F1); starting Urras again brings the open one forward
- Discarded files are copied first and kept for a week, since Git can't undo a discard
- Themes: built-in Dark and Light, and your own colours in shareable theme files that newer versions keep up to
  date ([docs/themes.md](docs/themes.md))

</details>

## Screenshots

<details>
<summary><b>Show the screenshots</b></summary>

<img src="docs/screenshots/history-graph.png" alt="Screenshot: history graph"><br>
History: the commit graph with a lane per branch, branch labels, authors with their pictures, and the commit and
date of each change

<table>
  <tr>
    <td>
      <img src="docs/screenshots/branch-menu.png" alt="Screenshot: branch menu"><br>
      Branch menu: branches, merge, rebase, pull requests, tags, reflog, submodules, worktrees and more
    </td>
    <td>
      <img src="docs/screenshots/repository-menu.png" alt="Screenshot: repository menu"><br>
      A repository's menu: open it elsewhere, view it on GitHub, fetch, pull, pin and group
    </td>
  </tr>
  <tr>
    <td>
      <img src="docs/screenshots/file-filter.png" alt="Screenshot: file filter"><br>
      Filtering the changes by name, with syntax colours in the diff
    </td>
    <td>
      <img src="docs/screenshots/markdown-preview.png" alt="Screenshot: markdown preview"><br>
      Markdown rendered instead of shown as text (Preview)
    </td>
  </tr>
  <tr>
    <td>
      <img src="docs/screenshots/import-menu.png" alt="Screenshot: import menu"><br>
      Import: everything in a folder, GitHub Desktop's list and fork choices, or clones from your accounts
    </td>
    <td>
      <img src="docs/screenshots/import-dialog.png" alt="Screenshot: import dialog"><br>
      Choosing which of the repositories found to add
    </td>
  </tr>
  <tr>
    <td>
      <img src="docs/screenshots/options-integrations.png" alt="Screenshot: options integrations"><br>
      Options: your editor and shell for "Open in…"
    </td>
    <td>
      <img src="docs/screenshots/options-git.png" alt="Screenshot: options git"><br>
      Options: your Git name, email (or GitHub's private address) and default branch
    </td>
  </tr>
  <tr>
    <td>
      <img src="docs/screenshots/options-appearance.png" alt="Screenshot: options appearance"><br>
      Options: ready-made themes (GitHub, GitLab, SourceGit, Fork) with their colours, and your own
    </td>
    <td>
      <img src="docs/screenshots/options-prompts.png" alt="Screenshot: options prompts"><br>
      Options: which questions to ask, and what happens to changes when switching branch
    </td>
  </tr>
  <tr>
    <td>
      <img src="docs/screenshots/options-advanced.png" alt="Screenshot: options advanced"><br>
      Options: background fetch and checks before committing
    </td>
    <td>
      <img src="docs/screenshots/git-log.png" alt="Screenshot: git log"><br>
      Git log: every command Urras runs, and what Git answered
    </td>
  </tr>
</table>

</details>

## For maintainers: build types and releases

<details>
<summary><b>Build types</b></summary>

Builds default to Release, which is optimised and still has full debug symbols, so breakpoints and stack traces
work (.NET's equivalent of CMake's RelWithDebInfo). Use `-c Debug` for an unoptimised build. Visual Studio and Rider
pick the configuration in their toolbar instead; choose Release there once and they remember it.

</details>

<details>
<summary><b>Publishing a release</b></summary>

Push a version tag; the Release workflow tests Urras, builds the Windows and Linux downloads and publishes them with
notes made from the changes since the last release:

```
git tag v0.2.0
git push origin v0.2.0
```

A tag with a hyphen, such as `v0.2.0-beta.1`, becomes a pre-release.

</details>

## Project layout and settings

```
src/Urras.Core/          Git commands, output parsers, settings. No UI.
src/Urras.App/           Avalonia user interface.
tests/Urras.Core.Tests/  Tests for Urras.Core, with a small built-in runner.
tests/Urras.App.Tests/   Interface tests, run without showing any window.
tools/Urras.Standards/   The coding standards check.
```

Settings (the repository list and everything in Options) are stored in `%APPDATA%\Urras\settings.json` on Windows
and `~/.config/Urras/settings.json` on Linux. Next to them are the daily settings backups, `crash.log` and
`startup.log`.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request: it covers the coding standards, commit
messages, tests and the contributor terms. AI-assisted pull requests are allowed, but must be disclosed in the pull
request template.

## Credits

- [Avalonia](https://github.com/AvaloniaUI/Avalonia) (MIT) for the user interface.

## License

Copyright (C) 2026 TheSCREWEDSoftware

Urras is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public
License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later
version.

Urras is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License along with Urras, in [LICENSE](LICENSE). If
not, see <https://www.gnu.org/licenses/>.
