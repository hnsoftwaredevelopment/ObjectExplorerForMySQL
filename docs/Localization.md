# Localization

The extension follows the user interface language of Visual Studio (ADR-010). Version 1 ships English only. This page describes how to add a language later.

## Which languages are possible

Visual Studio itself is available in 14 languages. Only these can ever be shown, because the extension follows the Visual Studio language:

| Language | Culture folder / resx suffix |
|---|---|
| Chinese (Simplified) | `zh-Hans` |
| Chinese (Traditional) | `zh-Hant` |
| Czech | `cs` |
| English | neutral (no suffix) |
| French | `fr` |
| German | `de` |
| Italian | `it` |
| Japanese | `ja` |
| Korean | `ko` |
| Polish | `pl` |
| Portuguese (Brazil) | `pt-BR` |
| Russian | `ru` |
| Spanish | `es` |
| Turkish | `tr` |

Dutch is not on the list: Visual Studio has no Dutch user interface.

## Where text lives

| Text | File |
|---|---|
| Tool window, dialogs, messages, Output pane name | `src/ObjectExplorerForMySql.Vsix/Resources/Strings.resx`, read through `Strings.cs` |
| Menu items, command names, option names and descriptions | `src/ObjectExplorerForMySql.Vsix/.vsextension/string-resources.json`, referenced in code as `"%Key%"` |
| Properties window: object types, categories and property names | `src/ObjectExplorerForMySql.Core/Resources/PropertyTexts.resx`, read through `PropertyTexts.Get`; used by `[LocalizedCategory]` and `[LocalizedDisplayName]` (E04) |
| Tool window toolbar and context menus | `src/ObjectExplorerForMySql.Vsix/ObjectExplorerCommands.vsct` (ADR-011); translated with a copy per culture, for example `ObjectExplorerCommands.de.vsct`, compiled with its own `VSCTCompile` item |
| Extension name and description in *Manage Extensions* | `source.extension.vsixmanifest`, translated with an `Extension.vsixlangpack` per culture |
| Log entries in the Output pane | English only, in code (ADR-005) |

## Adding a language (example: German)

1. Copy `Resources/Strings.resx` to `Resources/Strings.de.resx` and translate every value. Keep the keys.
   Do the same for `src/ObjectExplorerForMySql.Core/Resources/PropertyTexts.resx` (`PropertyTexts.de.resx`).
2. Create `.vsextension/de/string-resources.json` with the same keys as the default file and translated values. Add it to the VSIX project as `Content` with `IncludeInVSIX="true"`, like the default file.
3. Optional: add `de/Extension.vsixlangpack` with the translated display name and description, included in the VSIX.
4. Run the unit tests. `ResourceConsistencyTests` fails when a key is missing, unknown or empty in any language file.
5. Manual test: install the German language pack with the Visual Studio Installer, choose *Tools > Options > Environment > International Settings > Deutsch*, restart, and check the explorer window, the View menu item and the option page.

## Rules for developers

- Never put user-visible text in code or XAML. Add a key to `Strings.resx`, a property to `Strings.cs`, and use `Strings.Key` (XAML: `{x:Static res:Strings.Key}`).
- Command, menu and option texts use `"%ObjectExplorerForMySql.<Area>.<Name>%"` placeholders and a matching entry in `string-resources.json`.
- `ResourceConsistencyTests` checks both rules for keys; it cannot detect a hardcoded literal, so the self-review checklist still applies.
