# Fonts (SIL Open Font License 1.1)

Static instances cut from the variable fonts in [google/fonts](https://github.com/google/fonts) with fontTools
(`fontTools.varLib.instancer`), because Unity cannot import woff2 and its variable-font support is limited.
Neither font declares a Reserved Font Name, so the instances keep their family names. Licences are next to the files.
See `docs/architecture/adr-0003-open-source-fonts.md`.

| File | Source | Instance |
|---|---|---|
| `Manrope-Regular.ttf` | `ofl/manrope/Manrope[wght].ttf` | wght 400 |
| `Manrope-Bold.ttf` | same | wght 700 |
| `BricolageGrotesque-SemiBold.ttf` | `ofl/bricolagegrotesque/BricolageGrotesque[opsz,wdth,wght].ttf` | wght 600, opsz 96, wdth 100 |
| `BricolageGrotesque-Bold.ttf` | same | wght 700, opsz 96, wdth 100 |

Regenerate:
```
pip install fonttools
fonttools varLib.instancer "Manrope[wght].ttf" wght=700 -o Manrope-Bold.ttf --update-name-table
```
