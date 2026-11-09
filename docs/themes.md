# Themes

Urras comes with Dark and Light colours. A custom theme changes any of its colours on top of one of them.

## Making one

1. Open **Options > Appearance**.
2. Choose **New theme…** and give it a name. It starts as a copy of the dark or light colours, whichever look is
   chosen above.
3. Change the colours in the editor that opens. Each row shows a swatch, what the colour is used for, and its
   value as `#RRGGBB`, or `#AARRGGBB` for a see-through colour.
4. Pick the theme under **Colours** and save Options.

## Theme files

Each theme is a JSON file in the `themes` folder beside Urras's settings (`%APPDATA%\Urras\themes` on Windows).
**Open themes folder** in Options goes there. Copy a file to share a theme; put one in the folder to use it.

```json
{
  "Name": "Sunset",
  "Base": "Dark",
  "Colours": {
    "Text": "#F0E6DC",
    "MutedText": "#A08C7D",
    "Accent": "#FF9E64"
  }
}
```

`Base` is `Dark` or `Light`: it sets the look of buttons and lists, and the colour of anything the file leaves
out. When a newer version of Urras adds colours, it writes them into every theme file at start-up, with the
base's values, so a theme never misses one. Colours that aren't valid are replaced the same way.

The editor lists every colour with what it's used for; the names are the keys in the file.
