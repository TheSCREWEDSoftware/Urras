# Themes

Urras comes with Dark and Light colours. A custom theme changes any of its colours on top of one of them.

**Colours** also offers ready-made themes, the same as
[Anarres](https://github.com/TheSCREWEDSoftware/Anarres)'s: Anarres, Frost, Fel, Horde, Alliance and Parchment,
and GitHub Dark, GitHub Dark Dimmed, GitHub Light, GitLab Dark, SourceGit Dark and Fork Dark. Theme files use the
same format in both apps, so a file works in either; colours one app doesn't know are left alone by the other.

## Making one

1. Open **Options > Appearance**.
2. Choose **New theme…** and give it a name. It starts as a copy of the dark or light colours, whichever look is
   chosen above.
3. Change the colours in the editor that opens. Each row shows a swatch, what the colour is used for, and its
   value as `#RRGGBB`, or `#AARRGGBB` for a see-through colour.
4. Pick the theme under **Colours** and save Options.

**Edit…** changes the theme chosen under **Colours**, ready-made ones and the built-in colours included. Your
version of a ready-made theme (or of Dark or Light) is saved as a file with its name and used instead of it;
**Restore defaults** in the editor brings back the original colours, and saving them removes your version. The
editor's heading says which theme you're changing and where it's saved. Anarres works the same way.

## Font and text size

**Options > Appearance** sets the text size and font for every theme (any font installed on the computer), and,
under "Text, this theme only", a size or font for the theme chosen under **Colours**; "Same as every theme"
follows the general ones. **Use this theme's text for every theme** makes the chosen theme's text the general one
and clears every theme's own choice. These are saved in Urras's settings, not in the theme files.

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
