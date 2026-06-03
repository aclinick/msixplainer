---
permalink: /privacy/
title: MSIXplainer Privacy Policy
---

# Privacy Policy

**Last updated:** June 2, 2026
**App:** MSIXplainer
**Publisher:** Clinick

## Summary

MSIXplainer is a **fully local, offline** desktop tool. It does not collect, transmit, store, or share any personal information or usage data with the publisher or any third party. Everything you do in the app stays on your device.

## What data the app processes

When you open an MSIX, APPX, MSIXBUNDLE, or APPXBUNDLE file, MSIXplainer reads the package's `AppxManifest.xml` and `AppxBlockMap.xml` entries on your local device to produce its analysis report. The packages you analyze are **never uploaded, copied, or transmitted anywhere**. The app does not execute any code contained in the packages it inspects.

## What data the app collects

**None.** MSIXplainer:

- Does **not** make any network requests.
- Does **not** include any telemetry, analytics, crash reporting, or usage tracking SDKs.
- Does **not** create user accounts or require sign-in.
- Does **not** read any files you do not explicitly open via the file picker or pass on the command line.
- Does **not** share any data with the publisher or any third party.

## Local files the app may create

To save your preferences and exported reports on the device, MSIXplainer may write files to:

- `%LOCALAPPDATA%\MSIXplainer\` — optional severity overrides (`rules.json`) you create from within the app.
- Any folder you explicitly choose when using the **Export Markdown** or **Export JSON** features.

These files remain on your device and are never transmitted.

## Children

MSIXplainer is a developer / IT professional tool and is not directed at children.

## Changes to this policy

If the app ever begins collecting any data in the future, this policy will be updated and the change will be called out in the release notes published on GitHub.

## Contact

Questions about this policy can be filed as an issue at:
<https://github.com/aclinick/msixexplainer/issues>
