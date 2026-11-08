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
15. [x] Image diffs side by side (swipe and onion skin views still to do)
16. [ ] Syntax highlighting
17. [ ] Markdown preview for .md files, with a Diff / Preview switch (Markdown.Avalonia, MIT, approved)

## Phase 3: Everyday Git actions

18. [ ] Tags: create, delete, push
19. [ ] Check out a specific commit
20. [ ] Revert a commit
21. [ ] Cherry-pick a commit
22. [ ] Stashes: save, apply, pop, drop, and shown in the history
23. [ ] Merge a branch, with a conflict helper (take ours, take theirs, open in editor, mark resolved)
24. [ ] Rebase onto a branch
25. [ ] Repository settings: write .gitattributes and .editorconfig (LF, CRLF, or CRLF for Windows scripts and LF
    elsewhere), normalize existing files with a preview, optional check before committing

## Phase 4: Look into the past

26. [ ] File and directory history
27. [ ] Blame
28. [ ] Browse the file tree at any commit
29. [ ] Search commits by message, author, file or content
30. [ ] Compare two commits or branches
31. [ ] Reflog, to restore lost commits

## Phase 5: The big ones

32. [ ] Stage individual lines and hunks
33. [ ] Commit graph with branch lanes
34. [ ] Interactive rebase: pick, squash, reword, drop, drag to reorder, backup tag
35. [ ] Side-by-side conflict view: theirs, ours and result, with a choice per block
36. [ ] Rendered Markdown diff: old and new rendered side by side with changed blocks highlighted

## Phase 6: Project needs

37. [ ] Git LFS: status, pull, locks
38. [ ] Submodules and worktrees
39. [ ] Quick Launch (Ctrl+P)
40. [ ] Bisect

## Phase 7: Requires much

41. [ ] Fully editable merge resolver
42. [ ] Accounts window; tokens stored in Windows Credential Manager or the Linux keyring, never in settings
43. [ ] GitHub: sign in, repository list for cloning, avatars, pull requests, notifications
44. [ ] GitHub Enterprise Server (GitHub plus a server address)
45. [ ] GitLab and GitLab Server
46. [ ] Gitea
47. [ ] Bitbucket
48. [ ] Bitbucket Server (a separate API)
49. [ ] Custom commands
50. [ ] Statistics

Pushing and pulling already work with every host through Git Credential Manager; accounts only add the website
features.
