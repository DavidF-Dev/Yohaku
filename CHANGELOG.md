# Changelog

All notable changes to Yohaku are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-06-20

### Added

- Reserve a configurable margin around every monitor by registering four appbars per
  monitor, so maximised and snapped windows inset from the screen edges while staying
  genuinely maximised (real `SIZE_MAXIMIZED`, correct restore glyph, no injection).
- Per-edge insets (`InsetTop`, `InsetRight`, `InsetBottom`, `InsetLeft`) in logical
  pixels, scaled per monitor by its DPI, hot-reloaded from
  `%APPDATA%\Yohaku\config.json` on save.
- `TaskbarInset`: an optional separate inset for whichever edge holds the taskbar,
  applied only where the taskbar actually reserves space (an auto-hidden taskbar falls
  back to the per-edge inset). It re-resolves in place on an auto-hide toggle or config
  edit, with no teardown flash.
- Automatic rebuild on monitor add/remove and resolution/DPI changes, with all
  reservations released on exit.
- System-tray menu: edit config, reload config, rebuild margins, open log folder,
  About, and Exit.
- "Start with Windows" toggle that adds or removes a per-user
  `HKCU\...\CurrentVersion\Run` entry, self-healing the path if the executable moves.
- "Already running" feedback: launching a second instance surfaces a tray balloon from
  the running instance instead of exiting silently.
- Single-instance guard, rolling log file, and global exception logging.
