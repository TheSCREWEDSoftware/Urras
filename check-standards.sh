#!/bin/sh
# Checks files and commit messages against CODING_STANDARDS.md; run with --help for the options
cd "$(dirname "$0")" || exit 2

if [ $# -gt 0 ]; then
  exec dotnet run --project tools/Urras.Standards -v q -- "$@"
fi

echo "What should be checked?"
echo "  1) Uncommitted changes"
echo "  2) The last commit"
echo "  3) Commits not pushed yet"
echo "  4) Every commit"
echo "  5) Commits after a commit"
echo "  6) Commits since a date"
echo "  7) Every file as it is now"
echo "  8) Check every commit automatically from now on"
printf "Choice: "
read -r choice

case "$choice" in
  1) set -- --uncommitted ;;
  2) set -- --last ;;
  3) set -- --unpushed ;;
  4) set -- --history ;;
  5) printf "Commit (hash, tag or branch): "; read -r value; set -- --since-commit "$value" ;;
  6) printf "Date (such as 2026-11-01 or \"2 weeks ago\"): "; read -r value; set -- --since-date "$value" ;;
  7) set -- --all ;;
  8) set -- --install-hooks ;;
  *) echo "No such choice."; exit 2 ;;
esac

exec dotnet run --project tools/Urras.Standards -v q -- "$@"
