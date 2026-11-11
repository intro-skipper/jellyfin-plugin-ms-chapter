# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- Add an opt-in `PreferXmlChapters` option: an existing chapter XML wins over segment-generated chapters, is never overwritten, and is re-imported after metadata refreshes, so hand-authored chapters and segment-based skipping can coexist.
- Rebuild against Jellyfin 12.0.0-rc4 to keep the plugin configuration page compatible with the Jellyfin 12 web client.

## [0.6.0] - 2026-07-10

- Target Jellyfin 12 compatibility with .NET 10 and Jellyfin 12.0.0-rc2 packages.

## [0.4.0] - 2025-01-05

- Initial Release
