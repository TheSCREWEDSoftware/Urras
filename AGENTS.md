# AGENTS.md

Urras is a Git client for Windows and Linux, written in C# on .NET 10 with an Avalonia user interface. It runs the
installed `git` for every operation.

## Agent rules

- Follow `CODING_STANDARDS.md`. Formatting follows `.editorconfig`: UTF-8, LF, max 120 columns, trailing newline,
  no trailing whitespace; 4-space indent (tabs forbidden), 2-space for JSON/YAML/sh/XML/csproj/axaml.
- Comments are one line, and only where the code isn't self-explanatory.
- Keep code portable: no OS-specific APIs outside `OperatingSystem` checks, `System.IO` for paths, exact file-name
  case.
- Do not add dependencies, commit or push unless explicitly asked.
- `Urras.Core` must not depend on Avalonia or any UI code. Every change to Git behaviour needs a test in
  `tests/Urras.Core.Tests`, run against a real temporary repository.
- All Git commands go through `GitRunner`. Never add an operation that can lose data without a confirmation in the UI.
- Commit messages follow `.git_commit_template.txt`.
- Urras is licensed under the AGPL-3.0-or-later (`LICENSE`). Code taken from another project must credit its author
  and have a compatible license; ask first. Studying other Git clients is fine; copying their code is not.
- Planning docs go in `.agents/plans/<task-slug>/` (gitignored), named `<task-slug>.<TYPE>.md`
  (`PLAN`, `REQUIREMENTS`, `ANALYSIS`, …).

## Mandatory reading per task

- Writing or modifying C# → `CODING_STANDARDS.md`
- Reusing outside code → `LICENSE` and `CONTRIBUTING.md`

## Persisting lessons

When a user correction reveals a rule that generalizes, offer to add it to this file or `CODING_STANDARDS.md`.
