# Chapter Creator for Jellyfin MediaSegments

Automatically convert Jellyfin media segments into Matroska chapter XML files for enhanced media navigation.

## Overview

This plugin converts existing media segments (like Intros and Outros) into standardized Matroska chapter XML files

## Requirements

- ⚠️ Jellyfin 10.10 or newer
- Writeable media library access (read-only libraries are not supported)

## Features

- Automatic conversion of Media Segments to Matroska chapter XML
- Support for multiple segment types:
  - Intros
  - Outros
  - ...
- Compatible media types:
  - TV Shows
  - Movies

## Installation instructions

1. Add the plugin repository to your Jellyfin server:
   ```
   https://manifest.intro-skipper.org/manifest.json
   ```
2. Navigate to the General section in the Jellyfin plugin catalog
3. Install the "MS Chapter" plugin
4. Restart your Jellyfin server

## Technical Details

The plugin generates Matroska-compliant chapter files that follow the [official Matroska chapter specifications](https://www.matroska.org/technical/chapters.html). Each chapter includes:

- Unique chapter identifiers
- Start and end timestamps
- Chapter titles
