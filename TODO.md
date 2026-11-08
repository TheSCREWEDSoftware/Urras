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

1. [ ] Repository groups and folders, with fetch and pull for a whole group
2. [ ] Smaller tab titles, and hints in empty lists
3. [ ] About dialog with the version and the AGPL notice
4. [ ] Recent commit messages and a subject length reminder
5. [ ] Use the repository's commit message template
6. [ ] Open in Explorer, terminal or editor; copy path and SHA
7. [ ] Filter boxes for changed files and branches
8. [ ] Hide untracked files
9. [ ] Automatic background fetch

## Phase 2: Better diffs

10. [ ] Word-level highlighting inside changed lines
11. [ ] Wrap or scroll long lines
12. [ ] Image diffs: side by side, then swipe and onion skin
13. [ ] Syntax highlighting

## Phase 3: Everyday Git actions

14. [ ] Tags: create, delete, push
15. [ ] Check out a specific commit
16. [ ] Revert a commit
17. [ ] Cherry-pick a commit
18. [ ] Stashes: save, apply, pop, drop, and shown in the history
19. [ ] Merge a branch, with a conflict helper (take ours, take theirs, open in editor, mark resolved)
20. [ ] Rebase onto a branch

## Phase 4: Look into the past

21. [ ] File and directory history
22. [ ] Blame
23. [ ] Browse the file tree at any commit
24. [ ] Search commits by message, author, file or content
25. [ ] Compare two commits or branches
26. [ ] Reflog, to restore lost commits

## Phase 5: The big ones

27. [ ] Stage individual lines and hunks
28. [ ] Commit graph with branch lanes
29. [ ] Interactive rebase: pick, squash, reword, drop, drag to reorder, backup tag
30. [ ] Side-by-side conflict view: theirs, ours and result, with a choice per block

## Phase 6: Project needs

31. [ ] Git LFS: status, pull, locks
32. [ ] Submodules and worktrees
33. [ ] Quick Launch (Ctrl+P)
34. [ ] Bisect

## Phase 7: Requires much

35. [ ] Fully editable merge resolver
36. [ ] Accounts window; tokens stored in Windows Credential Manager or the Linux keyring, never in settings
37. [ ] GitHub: sign in, repository list for cloning, avatars, pull requests, notifications
38. [ ] GitHub Enterprise Server (GitHub plus a server address)
39. [ ] GitLab and GitLab Server
40. [ ] Gitea
41. [ ] Bitbucket
42. [ ] Bitbucket Server (a separate API)
43. [ ] Custom commands
44. [ ] Statistics

Pushing and pulling already work with every host through Git Credential Manager; accounts only add the website
features.
