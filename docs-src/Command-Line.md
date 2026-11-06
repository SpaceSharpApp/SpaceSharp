`SpaceSharp.exe` takes a few arguments, for shortcuts, scripts and the Run box. Settings › General › Command line shows the same list inside the app.

| Command | Does |
|---|---|
| `SpaceSharp.exe` | Starts normally, on the start screen |
| `SpaceSharp.exe D:\` | Scans a drive or folder right away |
| `SpaceSharp.exe --scan D:\` | The same, explicit |
| `SpaceSharp.exe scan.sscan` | Opens a saved scan |
| `SpaceSharp.exe --open scan.sscan` | The same, explicit |
| `SpaceSharp.exe --compare old.sscan D:\` | Scans `D:\` and compares it with a saved scan, opening the Changes view |
| `SpaceSharp.exe --help` | Prints this list (`-h` and `/?` work too) |

**One window.** SpaceSharp runs one instance per session. A second launch hands its arguments to the running window over a local named pipe and exits, so `SpaceSharp.exe D:\` from Explorer's right-click entry or a script scans in the window you already have open.

**The Explorer entry.** Settings › General › "Scan with SpaceSharp" in Explorer adds the command to the right-click menu of folders, drives and the folder background, for the current user only.

**For developers.** `--mock` starts on a pretend PC with a sample drive and a comparison from yesterday, so every screen has something to show without scanning anything; `--scene <name> --shot <file.png> --size 1600x1200` poses one of the screenshot scenes and writes it ([Building from source](Building-from-Source#the-pretend-pc)).
