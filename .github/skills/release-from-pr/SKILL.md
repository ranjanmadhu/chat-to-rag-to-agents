---
name: release-from-pr
description: Create release artifacts from a merged PR link: use user-provided SemVer tag when supplied, otherwise suggest next SemVer and wait for confirmation; then create git tag, draft GitHub release notes, and update docs README roadmap row with Release and PR diff links using existing repository format. Use when user says release from PR, tag this milestone, publish release details, or update docs/README.md after merge.
trigger: /release-from-pr
---

# /release-from-pr

Automates the post-merge release workflow for this repository.

Input:
1. PR link (required), for example: https://github.com/ranjanmadhu/chat-to-rag-to-agents/pull/9
2. Release tag (optional but preferred), for example: 0.2.3
3. Milestone row hint (optional), for example: Phase 3 Milestone 05

Output:
1. Local git tag created using repository format (no v prefix)
2. GitHub release title and body draft
3. docs/README.md updated in roadmap table with Release and PR diff links
4. Optional commit for README update

## Required behavior

1. Never assume tag format with v-prefix in this repository.
2. Keep release tag format numeric SemVer, for example: 0.2.3.
3. Update docs/README.md in the existing table style only.
4. Do not rewrite unrelated sections in docs/README.md.
5. If any step cannot be completed automatically, stop and provide exact manual command.
6. If tag is not provided by user, suggest one and wait for explicit confirmation before any write action.

## Write actions

Treat these as write actions:
1. Creating a local git tag
2. Pushing a tag
3. Creating a GitHub release
4. Editing docs/README.md
5. Committing any file

If tag is missing, perform read-only discovery only, then stop and wait for user confirmation.

## Workflow

### Step 0 - Tag input gate

1. If user provides SemVer tag, validate format and continue.
2. If user does not provide tag:
	1. Suggest next patch tag from current latest tag.
	2. Print: "Proposed tag: <TAG>. Reply with confirm to continue or provide a different tag."
	3. Stop. Do not perform write actions until user confirms.

### Step 1 - Parse inputs and validate repository state

1. Extract owner, repo, and PR number from PR link.
2. Validate current repository root and print current branch.
3. Run:

```powershell
git status --short
git tag --list
```

4. If there are uncommitted changes, continue only for docs edits and ask before creating tag.

### Step 2 - Load PR metadata

Preferred order:
1. Use `gh` CLI if available.
2. Fallback to PR web page fetch.

Commands:

```powershell
gh pr view <PR_NUMBER> --json number,title,body,author,mergedAt,baseRefName,headRefName,url
```

If `gh` is unavailable, fetch:
1. PR title
2. PR description
3. Changed files summary

### Step 3 - Resolve release tag

1. If user supplied a tag, use it.
2. Else suggest next patch from latest tag:

```powershell
git tag --sort=v:refname
```

3. Validate tag does not already exist.
4. If this is an auto-suggested tag, require explicit confirmation before write actions.

### Step 4 - Build release notes draft

Use the template in assets/release-notes-template.md.

Fill:
1. Release title: `<TAG> - <PR title>`
2. Summary from PR title/body
3. Key implementation bullets from changed files
4. Learning guide reference if included in PR
5. PR link and diff link

### Step 5 - Update docs/README.md roadmap row

Find the matching milestone row in the Learning Roadmap table.

Update only these cells:
1. Release column: `[<TAG>](https://github.com/<OWNER>/<REPO>/releases/tag/<TAG>)`
2. Diff column: `[PR #<N> diff](https://github.com/<OWNER>/<REPO>/pull/<N>/changes)`

Rules:
1. Preserve existing column order and markdown table alignment style.
2. Do not change Learning Guide path unless user explicitly asks.
3. If row cannot be uniquely identified, ask user for Phase/Milestone.

### Step 6 - Create tag locally

Only after confirmation of resolved tag:

```powershell
git tag <TAG>
```

Optional push:

```powershell
git push origin <TAG>
```

### Step 7 - Create GitHub release

Preferred:

```powershell
gh release create <TAG> --title "<TAG> - <PR title>" --notes-file <TEMP_RELEASE_NOTES_PATH>
```

Fallback manual instructions if `gh` unavailable:
1. Open `https://github.com/<OWNER>/<REPO>/releases/new`
2. Choose tag `<TAG>`
3. Paste generated release notes body
4. Publish release

### Step 8 - Commit docs update

If docs/README.md changed:

```powershell
git add docs/README.md
git commit -m "docs: update release and diff links for <TAG>"
```

Do not push unless user asks.

## Final response format

Provide:
1. Tag created status
2. Release created status
3. README update status
4. Exact changed row text in docs/README.md
5. Next command user may run (for example push tag)

## Safety checks

1. If tag already exists, stop and ask whether to reuse or choose a new tag.
2. If PR is not merged, stop and ask for confirmation.
3. If repository owner/repo in PR link does not match local git remote, stop and ask.
