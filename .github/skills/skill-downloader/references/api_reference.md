# GitHub URL Patterns

Use this reference to classify GitHub URLs before running the downloader.

## Web URLs

### Repo root
- https://github.com/{owner}/{repo}

### Folder in a repo
- https://github.com/{owner}/{repo}/tree/{ref}/{path}

### File in a repo
- https://github.com/{owner}/{repo}/blob/{ref}/{path}

The downloader treats both /tree and /blob URLs as a repository zip download, then extracts the given path.

## Raw URLs

### Raw file
- https://raw.githubusercontent.com/{owner}/{repo}/{ref}/{path}

Raw URLs point to a single file. Use them for .skill or .zip when available.

## Direct file links

### .skill or .zip
- https://host/path/to/file.skill
- https://host/path/to/file.zip

These are downloaded as files and saved to the output directory.

## Local paths

- C:\path\to\skill.skill
- C:\path\to\folder

Local folders are copied into the output directory as-is.
