#!/usr/bin/env python3
"""Download a skill from GitHub URLs, direct .skill/.zip links, or local paths."""

from __future__ import annotations

import argparse
import os
import re
import shutil
import sys
import tempfile
import urllib.parse
import urllib.request
import zipfile


def is_url(value: str) -> bool:
    try:
        parsed = urllib.parse.urlparse(value)
        return parsed.scheme in ("http", "https")
    except Exception:
        return False


def ensure_unique_path(path: str, force: bool) -> str:
    if force:
        if os.path.isdir(path):
            shutil.rmtree(path)
        elif os.path.isfile(path):
            os.remove(path)
        return path

    if not os.path.exists(path):
        return path

    base, ext = os.path.splitext(path)
    counter = 1
    while True:
        candidate = f"{base}-{counter}{ext}"
        if not os.path.exists(candidate):
            return candidate
        counter += 1


def download_to_file(url: str, dest_path: str) -> None:
    os.makedirs(os.path.dirname(dest_path), exist_ok=True)
    with urllib.request.urlopen(url) as response:
        with open(dest_path, "wb") as handle:
            shutil.copyfileobj(response, handle)


def copy_path(src_path: str, dest_path: str) -> str:
    if os.path.isdir(src_path):
        shutil.copytree(src_path, dest_path)
        return dest_path
    shutil.copy2(src_path, dest_path)
    return dest_path


def parse_github_url(url: str) -> dict | None:
    parsed = urllib.parse.urlparse(url)
    if parsed.netloc.lower() != "github.com":
        return None

    parts = [part for part in parsed.path.split("/") if part]
    if len(parts) < 2:
        return None

    owner, repo = parts[0], parts[1]
    ref = None
    subpath = ""

    if len(parts) >= 4 and parts[2] in ("tree", "blob"):
        ref = parts[3]
        subpath = "/".join(parts[4:]) if len(parts) > 4 else ""

    return {
        "owner": owner,
        "repo": repo,
        "ref": ref,
        "subpath": subpath,
    }


def parse_github_raw_url(url: str) -> dict | None:
    parsed = urllib.parse.urlparse(url)
    if parsed.netloc.lower() != "raw.githubusercontent.com":
        return None

    parts = [part for part in parsed.path.split("/") if part]
    if len(parts) < 4:
        return None

    owner, repo, ref = parts[0], parts[1], parts[2]
    subpath = "/".join(parts[3:])
    return {
        "owner": owner,
        "repo": repo,
        "ref": ref,
        "subpath": subpath,
    }


def extract_zip(zip_path: str, dest_dir: str) -> str:
    with zipfile.ZipFile(zip_path, "r") as archive:
        archive.extractall(dest_dir)
        top_levels = {
            name.split("/")[0]
            for name in archive.namelist()
            if name and "/" in name
        }

    if len(top_levels) == 1:
        return os.path.join(dest_dir, next(iter(top_levels)))

    # Fallback: return destination directory if structure is unexpected.
    return dest_dir


def resolve_zip_root_path(root_dir: str, subpath: str) -> str:
    if not subpath:
        return root_dir

    normalized = subpath.replace("/", os.sep)
    return os.path.join(root_dir, normalized)


def guess_filename_from_url(url: str, fallback: str) -> str:
    path = urllib.parse.urlparse(url).path
    name = os.path.basename(path)
    return name or fallback


def download_github_repo(owner: str, repo: str, ref: str, subpath: str) -> str:
    if not ref:
        ref = "main"

    zip_url = f"https://github.com/{owner}/{repo}/archive/refs/heads/{ref}.zip"
    temp_dir = tempfile.mkdtemp(prefix="skill_download_")
    zip_path = os.path.join(temp_dir, f"{repo}-{ref}.zip")
    download_to_file(zip_url, zip_path)
    root_dir = extract_zip(zip_path, temp_dir)
    target_path = resolve_zip_root_path(root_dir, subpath)

    if not os.path.exists(target_path):
        raise FileNotFoundError(f"Subpath not found in repo: {subpath}")

    return target_path


def handle_local(source: str, output_dir: str, name: str | None, force: bool) -> str:
    if not os.path.exists(source):
        raise FileNotFoundError(f"Local path not found: {source}")

    base_name = name or os.path.basename(os.path.normpath(source))
    dest_path = ensure_unique_path(os.path.join(output_dir, base_name), force)
    return copy_path(source, dest_path)


def handle_url(source: str, output_dir: str, name: str | None, force: bool, ref: str | None) -> str:
    github = parse_github_url(source)
    github_raw = parse_github_raw_url(source)

    if github:
        repo_ref = github["ref"] or ref or "main"
        temp_target = download_github_repo(
            github["owner"], github["repo"], repo_ref, github["subpath"]
        )
        base_name = name or os.path.basename(os.path.normpath(temp_target)) or github["repo"]
        dest_path = ensure_unique_path(os.path.join(output_dir, base_name), force)
        return copy_path(temp_target, dest_path)

    if github_raw:
        filename = name or os.path.basename(github_raw["subpath"]) or "downloaded_file"
        dest_path = ensure_unique_path(os.path.join(output_dir, filename), force)
        download_to_file(source, dest_path)
        return dest_path

    filename = name or guess_filename_from_url(source, "downloaded_file")
    dest_path = ensure_unique_path(os.path.join(output_dir, filename), force)
    download_to_file(source, dest_path)
    return dest_path


def main() -> int:
    parser = argparse.ArgumentParser(description="Download a skill from URLs or local paths.")
    parser.add_argument("source", help="GitHub URL, direct .skill/.zip URL, or local path")
    parser.add_argument(
        "--output",
        default=os.getcwd(),
        help="Output directory (default: current working directory)",
    )
    parser.add_argument(
        "--name",
        default=None,
        help="Optional override for output file or folder name",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing file or folder if it exists",
    )
    parser.add_argument(
        "--ref",
        default=None,
        help="Git ref to use for GitHub repo URLs (default: main)",
    )

    args = parser.parse_args()

    output_dir = os.path.abspath(args.output)
    os.makedirs(output_dir, exist_ok=True)

    try:
        if is_url(args.source):
            dest = handle_url(args.source, output_dir, args.name, args.force, args.ref)
        else:
            dest = handle_local(args.source, output_dir, args.name, args.force)
    except Exception as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1

    print(dest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
