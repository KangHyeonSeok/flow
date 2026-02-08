---
name: skill-downloader
description: Download skills from GitHub URLs, direct .skill/.zip links, or local paths. Use when a user provides a GitHub URL to a skill folder (including /blob or /tree), a repo root URL, a raw.githubusercontent.com URL, or a local/remote .skill or .zip that should be fetched and saved locally.
---

# Skill Downloader

## Overview

Download skills from supported sources and save them locally for later use.
Handle GitHub repo URLs, GitHub folder or file URLs, raw GitHub file URLs, direct .skill/.zip links, and local paths.

## Quick Start

Run the downloader script with a URL or local path:

```bash
python scripts/download_skill.py "https://github.com/anthropics/skills/blob/main/skills/skill-creator" --output .
```

The script prints the final saved path.

## Workflow

### 1) Classify the input

Determine whether the input is a local path or a URL. If it is a GitHub URL, identify which pattern it matches.
Use the patterns listed in references/api_reference.md.

### 2) Download and save

Use the script with the appropriate options:

```bash
python scripts/download_skill.py <source> [--output <dir>] [--name <name>] [--ref <git-ref>] [--force]
```

- For GitHub web URLs, pass the URL directly. The script downloads the repo zip and extracts the given path.
- For repo root URLs, use `--ref` if the branch is not `main`.
- For direct .skill or .zip links, the file is downloaded as-is.
- For local files or folders, the path is copied into the output directory.

### 3) Verify the result

Confirm the output path exists. If it is a folder, verify it contains SKILL.md at the top level.

## Troubleshooting

- If a GitHub download fails with 404, confirm the branch and use `--ref` to override.
- If a repo path is missing, confirm the URL points to the correct /blob or /tree path.
- If the output name conflicts, use `--name` or `--force`.

## Resources

- scripts/download_skill.py
- references/api_reference.md
