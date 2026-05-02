# Localization

amChipper keeps translations outside compiled UI logic so the interface can switch language live.

## Translation workflow

1. Open the language tool.
2. Load an existing language file from the release `lang/` folder.
3. Translate visible labels, menus, windows, buttons, tooltips, rack names, help text, and settings text.
4. Save as a new language file.
5. Test live language switching in the app.

## Quality rules

- Use native language names in the language selector.
- Avoid visible accelerator underscores unless the UI intentionally uses access keys.
- Translate non-main windows such as About, Tips, Export, Settings, and Help.
- Keep rack/editor names consistent across menus, tabs, help, and tooltips.
