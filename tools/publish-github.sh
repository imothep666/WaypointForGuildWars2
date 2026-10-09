#!/usr/bin/env bash
# Publishes the current state of main to the public GitHub repository.
#
#   tools/publish-github.sh            make the commit, show what it holds, push nothing
#   tools/publish-github.sh --push     the same, then push it
#
# The two repositories share no history, on purpose. Azure DevOps keeps the whole history, whose
# older commits carry a work address; GitHub only ever gets one fresh commit per publish, with
# main's files exactly as they are, authored with the GitHub noreply address. So a plain
# `git push` of main to GitHub would be wrong, and this script is the only way across.
#
# Each GitHub commit records the Azure commit it was made from (a "Source:" line), so the next
# publish can list what changed since. The local branch github-main holds the result.
#
# GitHub's address is GITHUB_URL, https by default; on a machine with a GitHub SSH key:
#   GITHUB_URL=git@github.com:imothep666/WaypointForGuildWars2.git tools/publish-github.sh --push

set -euo pipefail

GITHUB_URL="${GITHUB_URL:-https://github.com/imothep666/WaypointForGuildWars2.git}"
NAME=imothep666
EMAIL=imothep666@users.noreply.github.com
SOURCE=main

push=false
case "${1:-}" in
    --push) push=true ;;
    "") ;;
    *) echo "Unknown option: $1 (use --push or nothing)" >&2; exit 2 ;;
esac

cd "$(git rev-parse --show-toplevel)"

if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    echo "There are uncommitted changes. Commit them to $SOURCE first: only committed files are published." >&2
    exit 1
fi

# The one thing that must never cross: the address this machine's global git identity uses, or
# its domain, in a file. That is the work address on the machine this was written for. Read here
# rather than written down, so this public file does not name it. Commit metadata cannot carry it,
# since the commit below is made from scratch.
private=$(git config --global user.email || true)
if [[ -n "$private" && "$private" != "$EMAIL" ]]; then
    for needle in "$private" "${private#*@}"; do
        if git grep -q -i -I -F "$needle" "$SOURCE" -- . ; then
            echo "A file on $SOURCE contains '$needle', from your global git identity; not publishing." >&2
            echo "See: git grep -i -F '$needle' $SOURCE" >&2
            exit 1
        fi
    done
fi

# GitHub's main as it is now, whatever this machine last saw of it.
git fetch --quiet "$GITHUB_URL" main
parent=$(git rev-parse FETCH_HEAD)

tree=$(git rev-parse "$SOURCE^{tree}")
if [[ "$tree" == "$(git rev-parse "$parent^{tree}")" ]]; then
    echo "GitHub already has exactly what $SOURCE has. Nothing to publish."
    exit 0
fi

source_commit=$(git rev-parse "$SOURCE")
last=$(git log -1 --format=%B "$parent" | sed -n 's/^Source: \([0-9a-f]\{40\}\)$/\1/p' | tail -1)

message=$(mktemp)
trap 'rm -f "$message"' EXIT
{
    echo "Update to $(git rev-parse --short "$SOURCE")"
    echo
    if [[ -n "$last" ]] && git cat-file -e "$last^{commit}" 2>/dev/null; then
        git log --reverse --format='- %s' "$last..$SOURCE"
    else
        git log -1 --format='- %s' "$SOURCE"
    fi
    echo
    echo "Source: $source_commit"
} > "$message"

commit=$(GIT_AUTHOR_NAME=$NAME GIT_AUTHOR_EMAIL=$EMAIL GIT_COMMITTER_NAME=$NAME GIT_COMMITTER_EMAIL=$EMAIL \
    git commit-tree "$tree" -p "$parent" -F "$message")
git branch -f github-main "$commit"

echo "github-main is now $(git rev-parse --short "$commit"), on top of GitHub's $(git rev-parse --short "$parent"):"
echo
git log -1 --format='%B' "$commit" | sed 's/^/    /'
git diff --stat "$parent" "$commit" | tail -1

if $push; then
    git push "$GITHUB_URL" github-main:main
else
    echo
    echo "To publish it:  git push $GITHUB_URL github-main:main"
    echo "(or run this again with --push)"
fi
