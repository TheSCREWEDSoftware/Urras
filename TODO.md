# Urras TODO

Ordered by what to build next. Each phase builds on the one before it. Studying other Git clients (Fork, GitHub
Desktop) for ideas is fine; copying their code is not.

## Done

- [x] Repository list: add, create, clone, remove
- [x] Changes: stage, unstage, discard with confirmation
- [x] Commit and amend
- [x] History with commit diffs and column titles
- [x] Branches: switch, create, delete, check out remote branches
- [x] Fetch, pull and push with ahead and behind counts
- [x] Diff views: unified, split and raw, saved as a setting
- [x] Git log panel

## Phase 1: Finish the basics

1. [x] Repository groups and folders, with fetch and pull for a whole group
2. [x] Smaller tab titles, and hints in empty lists
3. [x] About dialog with the version and the AGPL notice
4. [x] Recent commit messages and a subject length reminder
5. [x] Use the repository's commit message template
6. [x] Open in Explorer, terminal or editor; copy path and SHA
7. [x] Filter boxes for changed files and branches
8. [x] Hide untracked files
9. [x] Automatic background fetch
10. [x] Line-ending warnings: flag files whose line endings don't match the repository's rules, in Changes and
    before committing

## Phase 2: Better diffs

11. [x] Word-level highlighting inside changed lines
12. [x] Show whitespace in diffs: spaces, tabs, trailing spaces and CRLF or LF at line ends (keep the \r when
    reading diffs)
13. [x] Changed files per file (file list with a diff per file) or combined, as a setting, in History and Changes
14. [x] Wrap or scroll long lines
15. [x] Image diffs side by side, as a swipe or as an onion skin
16. [x] Syntax highlighting (built in: C#, C/C++, shaders, JSON, XML/XAML/project files, CMake, Markdown,
    JavaScript/TypeScript, Python, shell, PowerShell, batch, YAML, INI/TOML, Lua)
17. [x] Markdown preview for .md files, with a Preview switch (Urras's own reader, so no new dependency)

## Phase 3: Everyday Git actions

18. [x] Tags: create, delete, push
19. [x] Check out a specific commit
20. [x] Revert a commit
21. [x] Cherry-pick a commit
22. [x] Stashes: save, apply, pop, drop and show, also listed in the history with their own badge
23. [x] Merge a branch, with a conflict helper (take ours, take theirs, open in editor, mark resolved)
24. [x] Rebase onto a branch
25. [x] Repository settings: write .gitattributes and .editorconfig (LF, CRLF, or CRLF for Windows scripts and LF
    elsewhere), normalize existing files with a preview, optional check before committing

## Phase 4: Look into the past

26. [x] File and directory history
27. [x] Blame
28. [x] Browse the file tree at any commit
29. [x] Search commits by message, author, file or content
30. [x] Compare two commits or branches
31. [x] Reflog, to restore lost commits

## Phase 5: The big ones

32. [x] Stage, unstage and discard individual lines and hunks (select lines in the diff, then right-click)
33. [x] Commit graph with branch lanes, all branches or the current one, and branch, remote and tag badges
34. [x] Interactive rebase: pick, reword, edit, squash, fixup, drop, reorder (drag, buttons or Alt+arrows),
    backup tag
35. [x] Side-by-side conflict view: theirs, ours and result, with a choice per block (right-click a conflicted
    file, Resolve conflicts…)
36. [x] Rendered Markdown diff: old and new rendered side by side with changed blocks highlighted

## Phase 6: Project needs

37. [x] Git LFS: files, tracked patterns, download, set up, lock, unlock and force unlock; Branch menu
38. [x] Submodules (list, open, update, add) and worktrees (list, open, create, remove, prune); Branch menu
39. [x] Quick Launch (Ctrl+P): repositories, branches and commands, matched by a few letters
40. [x] Bisect: start from History (one commit, or two selected), mark Good, Bad or Skip from the banner

## Phase 7: Requires much

41. [x] Fully editable merge resolver: type each conflict's result, or edit the whole file
42. [x] Accounts window; tokens stored in Windows Credential Manager or the Linux keyring, never in settings
43. [x] GitHub: sign in, repository list for cloning, avatars, pull requests, notifications
    - [x] Sign in with a personal access token (sign-in through the browser needs a registered OAuth app)
    - [x] Co-authors by @username, with suggestions while typing and GitHub's private noreply address
    - [x] Profile pictures in History and on co-author chips (your account, GitHub noreply addresses), cached on
      disk; coloured initials for everyone else
    - [x] Pictures for other emails (asking the platform who wrote a commit), remembered between runs
    - [x] Repository list for cloning (Import ▾ → Clone from your GitHub account), into Owner/Name folders
    - [x] Pull requests (list, check out, open, create) and notifications (list, open, mark as read)
44. [x] GitHub Enterprise Server (GitHub plus a server address), same features as GitHub
45. [x] GitLab and GitLab Server (merge requests, to-dos as notifications, pictures by email)
46. [x] Gitea (also Forgejo and Codeberg)
47. [x] Bitbucket (no user search, so no @username co-authors)
48. [x] Bitbucket Server (a separate API)
49. [x] Custom commands: your own programs and scripts in the Branch, file and commit menus and Quick Launch,
    with ${repo}, ${branch}, ${file}, ${filePath} and ${sha}
50. [x] Statistics: contributors, commits per month, day and hour, most changed files, by period
51. [ ] AI assistant with any provider: commit messages from the staged diff, explaining a commit, conflict
    suggestions. Providers: an OpenAI-compatible endpoint (OpenAI, OpenRouter, LM Studio, Ollama and others),
    Anthropic, or a command-line agent (Claude Code, Codex, Gemini CLI) given the diff on standard input. Keys in
    the credential store; off by default.

Pushing and pulling already work with every host through Git Credential Manager; accounts only add the website
features.
