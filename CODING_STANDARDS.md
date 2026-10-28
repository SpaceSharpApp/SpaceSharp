# Coding standards

How C# is written in SpaceSharp. The formatting itself (indentation, braces on their own lines, `using` order, spaces) is in `.editorconfig` and is applied by Rider, Visual Studio and `dotnet format`; this file is about the choices a formatter cannot make. Each rule has a **Don't** block and a **Do** block. Both are real: the "Don't" is how the code used to look and the "Do" is how it looks now, so you can open the file named and see the rule in place.

The one idea behind all of them: code is read far more often than it is written, usually by someone who did not write it and is in a hurry. Write for that reader.

Contents: [Names](#names) · [Nesting](#nesting) · [Methods](#methods) · [Blank lines and line breaks](#blank-lines-and-line-breaks) · [Braces](#braces) · [`var` and types](#var-and-types) · [Null](#null) · [Comments](#comments) · [XML documentation](#xml-documentation) · [Exceptions](#exceptions) · [What a rename must not touch](#what-a-rename-must-not-touch) · [Language](#language) · [Checklist](#checklist)

---

## Names

A name says what the value is. The reader should never have to scroll to where a variable is used to find out what it holds.

### Locals

**Don't** use letters, abbreviations or words that only say "a thing": `a`, `b`, `x`, `n`, `p`, `tmp`, `data`, `obj`, `res`, `val`, `item`, `info`.

```csharp
// MftScanner.ReadExact, before
int done = 0;
while (done < buffer.Length)
{
    int n = read(offset + done, buffer[done..]);
    if (n <= 0)
    {
        return false;
    }
    done += n;
}
```

**Do** say what the number counts.

```csharp
// MftScanner.ReadExact
int filled = 0;
while (filled < buffer.Length)
{
    int bytesRead = read(offset + filled, buffer[filled..]);
    if (bytesRead <= 0)
    {
        return false;
    }
    filled += bytesRead;
}
```

### Lambda parameters

The same rule applies inside a lambda. A one-letter parameter looks harmless on a short line and turns every longer lambda into a puzzle.

**Don't**

```csharp
var items = clearable.Where(s => _checkedCleanup.Contains(s.Key)).SelectMany(s => s.Items).Distinct().ToList();
var colors = look.Colors().ToDictionary(c => c.Key, c => Color.Parse(c.Hex));
pieces.Sort((a, b) => a.LowestVcn.CompareTo(b.LowestVcn));
```

**Do**

```csharp
var pickedItems = clearable
    .Where(suggestion => _checkedCleanup.Contains(suggestion.Key))
    .SelectMany(suggestion => suggestion.Items)
    .Distinct()
    .ToList();
var colors = look.Colors().ToDictionary(entry => entry.Key, entry => Color.Parse(entry.Hex));
pieces.Sort((first, second) => first.LowestVcn.CompareTo(second.LowestVcn));
```

For comparisons and sorts the pair is `(first, second)` or `(left, right)`. For a dictionary entry, `entry` or `pair`. For a node in the tree, `node`, `child`, `folder`, `file`, `ancestor`: whichever it is.

### Parameters

A parameter name is part of the method's documentation and shows up in every call that uses a named argument.

**Don't**

```csharp
private static void Validate(Geometry g)
private static Color Mix(Color a, Color b, double t)
private static void AddName(ref Entry entry, int parent, ushort parentSeq, string name)
```

**Do**

```csharp
private static void Validate(Geometry geometry)
private static Color Mix(Color start, Color end, double amount)
private static void AddName(ref Entry entry, int parent, ushort parentSequence, string name)
```

### Booleans

A boolean reads as a question or a state: `isPicked`, `hasScan`, `readFileTable`, `useElevatedHelper`, `belongsToMft`. Not `on`, `ok`, `set`, `flag`, `b`.

**Don't**

```csharp
bool mft = OperatingSystem.IsWindows() && _settings.FastNtfsScan && _scanExpectedBytes is not null && FileTableSupported(path);
bool elevate = !mft && OperatingSystem.IsWindows() && _settings.FastNtfsScan && _scanExpectedBytes is not null && !IsElevated && !_declinedElevation && IsNtfs(path);
```

**Do**

```csharp
bool fastScanWanted = OperatingSystem.IsWindows() && _settings.FastNtfsScan && _scanExpectedBytes is not null;
bool readFileTable = fastScanWanted && FileTableSupported(path);
// Without elevation, a helper process reads the file table.
bool useElevatedHelper = fastScanWanted && !readFileTable && !IsElevated && !_declinedElevation && IsNtfs(path);
```

### Units and roles

When the plain noun could mean two things, put the unit or the role in the name.

| Instead of | Write | Because |
|---|---|---|
| `length` next to a byte count | `lengthInClusters` | the two are easy to mix up |
| `info` | `previousScanInfo` | which scan? |
| `cts` | `scanAtRequest` | it is the scan that was running when the snapshot was asked for |
| `t` in drawing code | `gridThickness` | `t` also means time and interpolation |
| `a` in `Blend` | `combinedAlpha` | `a` also means "the first one" |
| `w`, `h` | `baseWidth`, `baseHeight` | which width? the control's, the map's, the box's? |

### Short names that are fine

- `i`, `j` for a plain loop index; `k` as a third one.
- The letters of a formula inside the method that is the formula: `r`, `g`, `b`, `h`, `s`, `l` in the HSL conversion in `Palette.cs`; `t` in a one-line `Lerp(from, to, t)`. Put a comment above the method saying the names follow the formula.
- `dc` for a `DrawingContext` and `ct` for a `CancellationToken`: the names the framework and the rest of the code already use.
- `_` for a parameter or deconstruction slot that is not used.
- `x`, `y` for coordinates when there is only one pair in scope. Two pairs get prefixes: `pivotX`, `cursorY`.

### Fields and members

Private fields are `_camelCase`; constants, `private static readonly` fields (brushes, regexes, lookup tables) and everything else `PascalCase`; interfaces `I`-prefixed. `.editorconfig` checks this and the build warns (IDE1006). Local functions get method names (`ShowStep`, `AddPattern`, `RenderChips`), not `Show`, `Add`, `Render`, and never a single letter such as `F` or `D`.

---

## Nesting

Every enclosing block is one more condition the reader must keep in mind. Handle the exceptional case first and leave; the normal path then runs down the left margin.

### Leave early

**Don't** wrap the body in the happy-path condition.

```csharp
// ElevatedScan, before
if (handle.IsInvalid)
{
    int code = Marshal.GetLastWin32Error();
    if (code == ErrorAccessDenied)
    {
        throw new UnauthorizedAccessException(Strings.Format("Mft_CannotOpen", _volume));
    }
    throw new IOException($"Cannot open volume {_volume}: {new Win32Exception(code).Message}", Marshal.GetHRForLastWin32Error());
}
geometry = VolumeData(handle);
```

**Do** return the good result as soon as it is known and let the error handling follow at the same level.

```csharp
// MftScanner.OpenVolume
SafeFileHandle handle = CreateFile(_volume, GenericRead, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
if (!handle.IsInvalid)
{
    return handle;
}

int errorCode = Marshal.GetLastWin32Error();
if (errorCode == ErrorAccessDenied)
{
    throw new UnauthorizedAccessException(Strings.Format("Mft_CannotOpen", _volume));
}
throw new IOException($"Cannot open volume {_volume}: {new Win32Exception(errorCode).Message}", Marshal.GetHRForLastWin32Error());
```

### Skip early in loops

**Don't**

```csharp
foreach (FsNode child in node.Children)
{
    if (child.IsReal)
    {
        ... twenty lines ...
    }
}
```

**Do**

```csharp
foreach (FsNode child in node.Children)
{
    if (!child.IsReal)
    {
        continue;
    }

    ... twenty lines at one level less ...
}
```

Only invert a condition this way when nothing follows the block inside the loop body; if something does, the `continue` would skip it.

### Never nest a ternary

A ternary is one choice between two values. Two of them inside each other is a decision tree written on one line.

**Don't**

```csharp
// TopLists, before
string what = n.BaselineSize is null ? Strings.Get(n.IsDirectory ? "Changes_NewFolder" : "Changes_New") : change > 0 ? Strings.Get(n.IsDirectory ? "Changes_GrewFolder" : "Changes_Grew") : Strings.Get(n.IsDirectory ? "Changes_ShrankFolder" : "Changes_Shrank");
```

**Do** write a method whose name says what the value is.

```csharp
// TopLists
private static string ChangeLabel(FsNode node, long change)
{
    if (node.BaselineSize is null)
    {
        return Strings.Get(node.IsDirectory ? "Changes_NewFolder" : "Changes_New");
    }
    if (change > 0)
    {
        return Strings.Get(node.IsDirectory ? "Changes_GrewFolder" : "Changes_Grew");
    }
    return Strings.Get(node.IsDirectory ? "Changes_ShrankFolder" : "Changes_Shrank");
}
```

The innermost `IsDirectory ? "...Folder" : "..."` is a single choice and stays a ternary. The same goes for a four-way chain:

**Don't**

```csharp
// SettingsWindow.KeepScansSlider, before
void Show(int i) => label.Text = KeepSteps[i] == 0 ? Strings.Get("KeepScans_Forever") : KeepSteps[i] % 365 == 0 ? Strings.Get("KeepScans_OneYear") : KeepSteps[i] >= 30 ? Strings.Format("KeepScans_Months", KeepSteps[i] / 30) : Strings.Format("KeepScans_Days", KeepSteps[i]);
```

**Do**

```csharp
// SettingsWindow.KeepScansSlider
void ShowStep(int index)
{
    int days = KeepSteps[index];
    if (days == 0)
    {
        label.Text = Strings.Get("KeepScans_Forever");
    }
    else if (days % 365 == 0)
    {
        label.Text = Strings.Get("KeepScans_OneYear");
    }
    else if (days >= 30)
    {
        label.Text = Strings.Format("KeepScans_Months", days / 30);
    }
    else
    {
        label.Text = Strings.Format("KeepScans_Days", days);
    }
}
```

### Name a compound condition

Past two or three terms, an `if` stops reading as a sentence. Put the condition in a `bool` whose name is the sentence.

**Don't**

```csharp
if ((eh.Flags & RecordInUse) == 0 || (eh.BaseReference & RecordMask) != 0 || (ushort)(eh.BaseReference >> 48) != header.Sequence
    || (eh.RecordNumber >= 0 && eh.RecordNumber != number) || eh.Sequence != (ushort)(reference >> 48))
{
    throw new MftException($"$MFT extension record {number} does not belong to $MFT.");
}
```

**Do**

```csharp
bool belongsToMft = (extensionHeader.Flags & RecordInUse) != 0
    && (extensionHeader.BaseReference & RecordMask) == 0
    && (ushort)(extensionHeader.BaseReference >> 48) == baseSequence
    && (extensionHeader.RecordNumber < 0 || extensionHeader.RecordNumber == number)
    && extensionHeader.Sequence == (ushort)(reference >> 48);
if (!belongsToMft)
{
    throw new MftException($"$MFT extension record {number} does not belong to $MFT.");
}
```

Stating the positive (`belongsToMft`) and testing `!belongsToMft` is easier to check than a chain of negated terms.

### A `switch` dispatches; it does not do the work

**Don't** put thirty lines in a `case`.

```csharp
// MftScanner.ParseAttributes, before
case AttrFileName when !nonResident:
    {
        ReadOnlySpan<byte> value = ResidentValue(attr);
        if (value.Length < 0x42)
        {
            break;
        }
        byte nameSpace = value[0x41];
        if (nameSpace == 2)
        {
            break;   // DOS-only 8.3 name
        }
        ... fifteen more lines ...
        break;
    }
```

**Do** call a method per case and keep the `switch` to one screen.

```csharp
// MftScanner.ParseAttributes
case AttrStandardInformation when !nonResident && isBase:
    ParseStandardInformation(attribute, ref entry);
    break;

case AttrFileName when !nonResident:
    ParseFileName(attribute, ref entry);
    break;

case AttrData when nameLength == 0 && !entry.HasData:
    ParseDataSizes(attribute, nonResident, ref entry);
    break;
```

---

## Methods

A method does one thing, and its name says which. If you need comments inside it to mark its parts ("// now the header", "// then the table"), the parts want to be methods.

### Split by step

**Don't** let a method grow by accretion. `ScanAsync` used to reset twelve controls of the scan card inline, then run the scan, then handle two kinds of fallback in nested `try`/`catch`, all in one body.

**Do** keep the orchestration in the method and give each step a name. Now `ScanAsync` reads:

```csharp
ResetScanCard(readFileTable);
_scanClock.Restart();
_progressTimer.Start();
...
FsNode root;
if (readFileTable && OperatingSystem.IsWindows())
{
    root = await ReadTreeFromFileTableAsync(path, options, useElevatedHelper, _scanCts.Token);
}
else
{
    root = await _scanner.ScanAsync(path, options, _scanCts.Token);
}
```

and the fallback chain is one method with one job:

```csharp
/// <summary>Reads the tree from the NTFS file table, falling back to a folder walk when that is declined or fails.</summary>
private async Task<FsNode> ReadTreeFromFileTableAsync(string path, WalkOptions options, bool useElevatedHelper, CancellationToken token)
{
    try
    {
        return useElevatedHelper
            ? await ScanElevatedAsync(path, options, token)
            : await ScanWithFileTableAsync(path, options, token);
    }
    catch (ElevatedScan.DeclinedException)
    {
        ...
        return await _scanner.ScanAsync(path, options, token);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
    {
        ...
        return await _scanner.ScanAsync(path, options, token);
    }
}
```

The same pattern runs through the code: `SettingsWindow.Build()` is a `switch` over tabs with a `Build<Tab>Tab()` each; `BuildTiles()` in the Insights view is one call per tile; `MftExtents` reads record 0, then `ReadNonResidentAttributeList`, then `AddExtensionDataPieces`, then `JoinPieces`.

### One expression, one place

**Don't** repeat an expression. The scan card's subtitle was assembled in four places, each slightly differently:

```csharp
ScanSubtitle.Text = (_scanExpectedBytes is { } bytes ? Strings.Format("Scan_Used", SizeFormatter.Format(bytes)) + " · " : string.Empty) + Strings.Get(mft ? "Scan_MethodMft" : "Scan_MethodWalk");
...
ScanSubtitle.Text = (_scanExpectedBytes is { } b4 ? Strings.Format("Scan_Used", SizeFormatter.Format(b4)) + " · " : string.Empty) + Strings.Get("Scan_MethodWalk");
...
ScanSubtitle.Text = (_scanExpectedBytes is { } b3 ? Strings.Format("Scan_Used", SizeFormatter.Format(b3)) + " · " : string.Empty) + Strings.Get("Scan_MethodWalk") + " · " + ex.Message;
```

**Do** write it once.

```csharp
/// <summary>The card's subtitle: the drive's used size, when known, and the scan method.</summary>
private string ScanSubtitleText(string methodKey)
{
    string usedPrefix = _scanExpectedBytes is { } expectedBytes
        ? Strings.Format("Scan_Used", SizeFormatter.Format(expectedBytes)) + " · "
        : string.Empty;
    return usedPrefix + Strings.Get(methodKey);
}

ScanSubtitle.Text = ScanSubtitleText(readFileTable ? "Scan_MethodMft" : "Scan_MethodWalk");
ScanSubtitle.Text = ScanSubtitleText("Scan_MethodWalk") + " · " + ex.Message;
```

### Pass what the method needs

An extracted method takes its inputs as parameters and is `static` when it touches no field. That makes it testable and makes its dependencies visible at the call site.

```csharp
private static (long LowestVcn, long HighestVcn, List<(long Offset, long Length)> Runs) DataPiece(ReadOnlySpan<byte> attribute, Geometry geometry, string what)
```

Not a method that reads three fields the caller just set.

### No abstraction for its own sake

**Don't** add an interface with one implementation, a base class for two windows that share three lines, a generic helper used once, or a "manager" class that holds what used to be two methods. Splitting a method is for reading; it is not a reason to introduce a type. The seams that do exist (`IPlatformFiles`, the `VolumeRead` delegate in `MftScanner`) are there because the tests substitute them.

### Hot paths

In `TreemapControl.Render.cs`, `TreemapControl.Layout.cs`, `Squarify.cs` and `Grouping.cs`, the per-box loop runs tens of thousands of times a frame. Extract whole steps there, never a fragment inside the loop, and do not add allocations, LINQ, closures or `string.Format` to it. Readability still applies to the names; it does not license a `Select` in a paint loop.

---

## Blank lines and line breaks

### One blank line between steps

Inside a method, a blank line marks where one logical step ends and the next begins: compute, then check, then act. Lines that belong together have no blank line between them. Two blank lines in a row is never right.

**Don't**

```csharp
long allocated = listRuns.Sum(run => run.Length);
if (listSize <= 0 || listSize > allocated || listSize > 0x1000000)
{
    throw new MftException("$MFT's attribute list has an invalid size.");
}
byte[] attributeList = new byte[allocated];
long filled = 0;
foreach ((long offset, long length) in listRuns)
{
    ...
}
Array.Resize(ref attributeList, (int)listSize);
return attributeList;
```

**Do**

```csharp
long allocated = listRuns.Sum(run => run.Length);
if (listSize <= 0 || listSize > allocated || listSize > 0x1000000)
{
    throw new MftException("$MFT's attribute list has an invalid size.");
}

byte[] attributeList = new byte[allocated];
long filled = 0;
foreach ((long offset, long length) in listRuns)
{
    ...
}

Array.Resize(ref attributeList, (int)listSize);
return attributeList;
```

### Break long calls

Past about 120 columns a line stops being readable without scrolling. Break the call after the opening parenthesis or after the first argument, and put one argument per line.

**Don't**

```csharp
if (await Dialog.Confirm(this, Strings.Get("Settings_Language_RestartTitle"), Strings.Format("Settings_Language_RestartMessage", name), Strings.Get("Settings_Language_RestartNow"), Strings.Get("Settings_Language_Later")))
```

**Do**

```csharp
bool restartNow = await Dialog.Confirm(this,
    Strings.Get("Settings_Language_RestartTitle"),
    Strings.Format("Settings_Language_RestartMessage", displayName),
    Strings.Get("Settings_Language_RestartNow"),
    Strings.Get("Settings_Language_Later"));
if (restartNow)
```

Naming the result (`restartNow`) also keeps the `if` to one line.

### LINQ one operator per line

A chain of more than two operators goes one per line, each starting with the dot.

```csharp
var largestFolders = root.DescendantDirectories()
    .Where(directory => !ReferenceEquals(directory, root)
        && !directory.Children.Any(child => child.IsDirectory && child.SizeFor(measure) == directory.SizeFor(measure)))
    .OrderByDescending(directory => directory.SizeFor(measure))
    .Take(7)
    .ToList();
```

### Initializers one property per line

An object initializer with more than two or three properties goes one per line.

**Don't**

```csharp
var a = new Border { Height = height, Background = fillFirst, MinWidth = 2, CornerRadius = second > 0 ? new CornerRadius(r, 0, 0, r) : new CornerRadius(r) };
```

**Do**

```csharp
var firstSegment = new Border
{
    Height = height,
    Background = fillFirst,
    MinWidth = 2,
    CornerRadius = second > 0 ? new CornerRadius(radius, 0, 0, radius) : new CornerRadius(radius)
};
```

### One declaration per line

**Don't** `double l = (max + min) / 2, h = 0, sat = 0;` or `int bytesPerRecord = g.BytesPerRecord, bytesPerCluster = g.BytesPerCluster;`

**Do**

```csharp
int bytesPerRecord = geometry.BytesPerRecord;
int bytesPerCluster = geometry.BytesPerCluster;
```

Constants are the exception when they form one table (`FlagInUse = 1, FlagDirectory = 2, ...`), with a comment saying what the table is.

---

## Braces

Every `if`, `else`, `for`, `foreach`, `while`, `using`, `try`, `catch` and `finally` body has braces on their own lines, even for a single statement. A body without braces is the easiest place to add a second statement that ends up silently outside the block.

**Don't**

```csharp
if (handle.IsInvalid) return false;
catch (IOException) { }
finally { _syncingFilterUi = false; }
foreach (var x in items) Add(x);
```

**Do**

```csharp
if (handle.IsInvalid)
{
    return false;
}

catch (IOException)
{
    // A temp file left behind in the scans folder is harmless; the tree is already loaded.
}

finally
{
    _syncingFilterUi = false;
}
```

A lambda with a body follows the same rule: `(_, _) => { _settings.CushionEnabled = enabled; SetEnabled(cushionRows, enabled); }` on one line becomes a block with one statement per line. A lambda that is a single expression stays an expression: `enabled => _settings.LiveMap = enabled`.

---

## `var` and types

`var` only where the right-hand side states the type by itself; the explicit type everywhere else, and always for the built-in types. These are not matters of taste in this repo: `.editorconfig` sets `csharp_style_var_when_type_is_apparent = true`, `csharp_style_var_for_built_in_types = false` and `csharp_style_var_elsewhere = false`, and both IDE0007 ("use var") and IDE0008 ("use explicit type") build as warnings, so the compiler says which of the two a line needs.

### Use `var` only when the type is apparent

"Apparent" is what the analyzer calls apparent: an object or array creation, a cast (including `as`), a static call on a type that returns that type (`CommandLine.Parse`, `Color.FromRgb`, `TopLevel.GetTopLevel`, `Process.Start`), and a conversion method named for its result (`ToList()`, `ToArray()`, `ToDictionary()`, `ToHashSet()`).

```csharp
var scanner = new MftScanner(path, includeHidden, exclude, progress);
var badge = new Border { ... };
var managedFiles = new[] { "hiberfil.sys", "pagefile.sys", "swapfile.sys" };
var arguments = CommandLine.Parse(rawArgs);
var topLevel = TopLevel.GetTopLevel(this);
var folders = root.DescendantDirectories().ToList();
var mainWindow = owner as MainWindow;
var topExtensions = totals.OrderByDescending(...).Select(entry => entry.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
using var process = Process.Start(startInfo);
```

Writing `List<FsNode> folders = root.DescendantDirectories().ToList();` says the type twice and warns (IDE0007).

### Write the built-in types out

A literal shows the value, not the type the code means by it: `0` can be an `int`, a `long` or a `double`, and the difference matters in byte counts and screen geometry. So `int`, `long`, `double`, `bool`, `string` and the rest are always written.

**Don't**

```csharp
var filled = 0;
var name = "C:";
var found = false;
```

**Do**

```csharp
int filled = 0;
string name = "C:";
bool found = false;
```

### Write the type when the right-hand side does not show it

A call on your own type, a property, a field, an element of a collection, arithmetic, a ternary, a LINQ chain that stops before a conversion method: none of those tell the reader the type, and the build warns about `var` on them (IDE0008).

**Don't**

```csharp
var p = _scanMethod == "MFT" ? _mftProgress : _scanner.GetProgress();
var seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);
var selected = SelectedChangeNode();
var value = ResidentValue(attribute);
```

**Do**

```csharp
WalkProgress progress = _scanMethod == "MFT" ? _mftProgress : _scanner.GetProgress();
double seconds = Math.Max(0.001, _scanClock.Elapsed.TotalSeconds);
FsNode? selectedNode = SelectedChangeNode();
ReadOnlySpan<byte> value = ResidentValue(attribute);
```

### Numeric types on purpose

Where the width or the sign matters, the type is explicit and chosen: `long` for a byte count or a file offset, `ulong` for an NTFS record reference, `ushort` for a sequence number, `double` for anything measured on screen, `int` for a count or an index. Never `int` for bytes, never `float`.

```csharp
long logical = checked(number * geometry.BytesPerRecord);
ulong baseNumber = header.BaseReference & RecordMask;
ushort parentSequence = (ushort)(parentReference >> 48);
double headerLeft = Math.Max(header.X, 0);
```

### Watch what `var` infers

`var` can hide a surprise. `ToHashSet(ReferenceEqualityComparer.Instance)` infers `HashSet<object>` because that comparer compares `object`; writing `HashSet<FsNode>` on the left does not compile. When `var` is the only thing that compiles, the type is not as obvious as it looks: that is a sign to read the line twice, not to add a cast.

---

## Null

The projects compile with nullable reference types on. The compiler's flow analysis is the one null check that never goes stale, so write code it can follow.

### Test, then use

**Don't** repeat the lookup and tell the compiler to trust you.

```csharp
// SettingsWindow.LanguageCombo, before
string name = cultures[i] is null ? AppLanguages.DisplayName(AppLanguages.SystemCulture) : AppLanguages.DisplayName(cultures[i]!);
```

**Do** read into a local once; the compiler then knows it is non-null on the other branch.

```csharp
CultureInfo? chosenCulture = cultures[index];
string displayName = chosenCulture is null
    ? AppLanguages.DisplayName(AppLanguages.SystemCulture)
    : AppLanguages.DisplayName(chosenCulture);
```

### Patterns instead of `!`

**Don't**

```csharp
var parents = s.Nodes.Select(n => n.Parent?.FullPath).Where(p => p is not null).Distinct().ToList();
... parents.Count == 1 ? parents[0]! : ...
```

**Do**

```csharp
var parentPaths = summary.Nodes.Select(node => node.Parent?.FullPath).OfType<string>().Distinct().ToList();
... parentPaths.Count == 1 ? parentPaths[0] : ...
```

`OfType<string>()` filters the nulls and changes the element type at the same time. Likewise `is { } value` binds and tests in one step:

```csharp
if (node.HasBaseline && BaselineChangePart(node, measure) is { } change)
{
    second.Add(change);
}
```

### The null-forgiving operator

`!` turns off the check and tells the reader nothing. Before writing one, try a local, a pattern, `OfType<T>()`, or `?? throw`. The uses that remain are where a guarantee exists that the compiler cannot see, and the name or a comment next to it says what the guarantee is:

- `_card!` in `SettingsWindow`: assigned by `Section(...)` before every `Row(...)`, by construction of the builder.
- `categories![i]` in `Palette.FromList`: `named` is true only when `categories` was passed.
- `summary.ByAge.MaxBy(band => band.Bytes)!`: the list is never empty.
- `entry.Name!` in `MftScanner.BuildTree`: `Candidate(ref entry, i)` returned false for every entry without a name two lines up.

`null!` for a field that `InitializeComponent` fills is the same idea. Anything else is a bug waiting for the case that was "impossible".

---

## Comments

A comment says why, or what the reader could not work out from the code. It never says what the next line does.

### Every method of substance has a summary

One sentence, present tense, no "This method". It says what comes back or what changes, not how.

```csharp
/// <summary>Orders the pieces by VCN, checks they are gap-free and cover the valid length, and returns their runs in one list.</summary>
/// <summary>Reads the tree from the NTFS file table, falling back to a folder walk when that is declined or fails.</summary>
/// <summary>The card's subtitle: the drive's used size, when known, and the scan method.</summary>
```

A `<remarks>` line follows when there is a condition the caller must know:

```csharp
/// <remarks>Each extension is read through the extents found so far, so an extension stored beyond them is unsupported.</remarks>
```

Public members always have one. Private one-liners and trivial builders (`Pixels(x)`, `Swatch(color)`) do not need one.

### Say why, not what

**Don't**

```csharp
// increment the counter
_files++;
// loop over the children
foreach (FsNode child in folder.Children)
// return the result
return result;
```

**Do** write the comment the reader will thank you for:

```csharp
// Walking backwards and pushing at the head keeps each folder's children in record order.
for (int i = count - 1; i >= 0; i--)
```

```csharp
// The counters start over: the provisional ones counted every record, these count what reaches the tree.
_files = 0;
_directories = 0;
_bytes = 0;
```

```csharp
// Compressed and sparse streams keep the on-disk size at 0x40; present when the runs start at or after 0x48.
// The compression unit is 0 for sparse-only streams, so it is not a usable test.
if ((attributeFlags & (AttrFlagCompressed | AttrFlagSparse)) != 0 && runsOffset >= NonResidentHeaderCompressed)
```

```csharp
// The snapshot shares the walk's finished folders, so it is sorted on the UI thread (in ShowLive), never here.
```

Each of these answers a question the code raises: why backwards? why reset? why 0x40? why not sort here?

### An ignored exception always says why

An empty `catch` without a comment looks like a mistake, and the next person cannot tell whether it is one.

**Don't**

```csharp
catch (IOException) { }
catch (UnauthorizedAccessException) { }
```

**Do**

```csharp
catch (IOException)
{
    // A temp file left behind in the scans folder is harmless; the tree is already loaded.
}
catch (UnauthorizedAccessException)
{
    // Same: the elevated helper wrote it, so this process may not be allowed to delete it.
}
```

```csharp
catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
{
    // Without the drive's used size the progress is shown as a sweep instead of a percentage.
}
```

```csharp
catch (OutOfMemoryException)
{
    // Turned into an MftException so the caller falls back to the folder walk instead of crashing.
    throw new MftException($"Not enough memory for a file table of {recordCount:N0} records.");
}
```

The comment names the consequence of ignoring it ("shown as a sweep", "falls back to the folder walk"), which is what the reader needs to judge whether ignoring is still right.

### Keep comments true

A comment that describes code that has since changed is worse than no comment: the reader believes it. When you change a line, read the comment above it. When you move code, move its comment. When a `switch` case becomes a method, the case comment goes on the method.

### No prose

Comments are one or two sentences. If a method needs three paragraphs to explain, the method is the problem, not the comment. Section markers in long files (`// ---- volume`) are fine; essays are not.

---

## XML documentation

The `///` blocks are what Rider shows on hover and what the next person reads before opening the method. They follow the comment rules above, plus these.

### Every documented method is documented completely

A block with a summary and nothing else is half a contract. Once a method has a `///` block it carries one `<param>` per parameter, in declaration order and with the exact parameter name, a `<returns>` when the method returns something, and a `<typeparam>` for each generic type parameter.

**Don't**

```csharp
/// <summary>Bytes a drive filled since a scan and whether that is notable.</summary>
public static (long FilledSince, bool Notable) DriftSince(long freeAtScan, long freeNow, long total)
```

**Do**

```csharp
// DriveInsights
/// <summary>Bytes a drive filled since a scan (negative when it emptied) and whether that is notable; unknown drives are never notable.</summary>
/// <param name="freeAtScan">Free bytes recorded in the saved scan's header; 0 or less means unknown (a folder scan).</param>
/// <param name="freeNow">Free bytes on the drive now.</param>
/// <param name="total">The drive's capacity in bytes; 0 or less means unknown.</param>
/// <returns>The change in used bytes (positive when the drive filled) and whether it reaches <see cref="NotableDrift"/>; (0, false) when either input is unknown.</returns>
public static (long FilledSince, bool Notable) DriftSince(long freeAtScan, long freeNow, long total)
```

Which members get a block: every public and internal method, constructor, property, type, delegate and enum; every private method of substance. Which do not: XAML event handlers (`Rescan_Click`, `TopBar_SizeChanged`), overrides whose base is documented (`Render`, `MeasureOverride`, `ToString`), one-line builders whose name says everything (`Pixels(x)`), and auto-properties whose name says everything.

### A `<param>` says what the name cannot

The reader already sees the name and the type. The tag adds the unit, the range, the coordinate space, what null or empty means, which order a list must have, or what a callback receives and when.

**Don't** restate the name.

```csharp
/// <param name="path">The path.</param>
/// <param name="bytes">The bytes.</param>
/// <param name="onChanged">The change callback.</param>
/// <param name="nodes">The nodes to lay out.</param>
```

**Do**

```csharp
// Squarify.Layout
/// <param name="nodes">Sorted largest first by <paramref name="measure"/>; the run of nodes before the first zero-sized one is laid out, the rest are ignored.</param>
/// <param name="bounds">The rectangle to fill, in whatever unit the caller draws in; nothing is emitted when its width or height is zero or negative.</param>
/// <param name="emit">Called once per laid-out node, in row order from the first row, with a rectangle inside <paramref name="bounds"/>; the call is synchronous on the caller's thread.</param>
/// <param name="orientationBias">1 is the plain algorithm; above 1 favors vertical strips, below 1 horizontal.</param>
```

```csharp
// SettingsWindow.PercentSlider
/// <param name="value">Initial value in percent.</param>
/// <param name="onChanged">Receives the rounded value on every change while dragging, then settings save and apply as in <see cref="Change"/>.</param>
/// <param name="max">Upper end of the range in percent; the lower end is 0.</param>
```

```csharp
// MftScanner.Progress
/// <param name="files">Files seen so far; provisional while reading (every in-use record), exact in the final call.</param>
/// <param name="currentPath">While reading, the localized "record n of m" text; then <see cref="BuildingMarker"/>; finally the root path.</param>
```

The questions each tag should answer, where they apply:

| Kind of parameter | Say |
|---|---|
| a number | the unit (bytes, clusters, pixels, percent, days, seconds) and the range (0..1, 0..100, -1..1) |
| a coordinate or size | the space it is in (normalized 0..1 map space, control pixels, screen pixels) |
| a path | drive root only, or any folder; full or relative; with or without the trailing separator |
| a time | UTC or local; `MinValue` meaning unknown |
| a collection | the required order; what empty means; whether it is read or mutated |
| a nullable | what null means (default, "all", "unknown", "not applicable") |
| a callback | what it receives, how often, on which thread, and whether it may throw |
| a token | what it cancels and how often it is checked |
| a flag | what each value does, not "whether to ..." |
| a pass-through | what it is passed to |

Everything in a tag is read off the body and the callers. A tag that guesses is worse than none: the compiler cannot check it and the reader will trust it.

### `<returns>` covers the edge cases

The normal case is usually in the summary already. The tag's job is the rest: null when, empty when, -1 when, false when; for a tuple, what each part is.

**Don't**

```csharp
/// <returns>The result.</returns>
/// <returns>True if successful.</returns>
/// <returns>A list of nodes.</returns>
```

**Do**

```csharp
/// <returns>The bytes read into the buffer; 0 at the end of the volume.</returns>
/// <returns>The value the dialog closed with, or default when it closed without one (the title bar X, Escape).</returns>
/// <returns>True when the bin is empty afterwards (an already empty bin counts); false elsewhere or on a shell error.</returns>
```

### `<typeparam>` on every generic

```csharp
// Modal.ShowModal
/// <summary>Shows the dialog over its owner and waits for its result.</summary>
/// <typeparam name="T">The type the dialog passes to <c>Close(result)</c>.</typeparam>
/// <param name="dialog">The window to show; it is closed by its own buttons, not by this call.</param>
/// <param name="owner">The window to block; only a <see cref="MainWindow"/> owner is dimmed, any other owner just shows the dialog.</param>
/// <returns>The value the dialog closed with, or default when it closed without one (the title bar X, Escape).</returns>
public static async Task<T?> ShowModal<T>(this Window dialog, Window owner)
```

### Records, delegates, enums and P/Invoke

A positional record documents each positional parameter with `<param>` on the record itself; a delegate documents its parameters and return like a method; an enum gets a summary, and a summary per member when the name alone does not say it (`MapDensity` members name their thresholds). A P/Invoke declaration gets a one-line summary naming what the Win32 function is used for here; its parameters follow the Win32 meaning.

```csharp
/// <summary>Volume geometry from FSCTL_GET_NTFS_VOLUME_DATA.</summary>
/// <param name="BytesPerSector">Sector size in bytes; a power of two from 512 to 64K.</param>
/// <param name="BytesPerCluster">Cluster (allocation unit) size in bytes; a power of two, at least a sector.</param>
/// <param name="MftValidLength">The initialized length of $MFT in bytes; records beyond it were never written.</param>
internal readonly record struct Geometry(int BytesPerSector, int BytesPerCluster, int BytesPerRecord, long TotalClusters, long MftValidLength, long MftStartLcn);
```

### `<exception>` where the caller must handle it

When a method throws something the caller is expected to catch, say which and when. `MftScanner` throws `MftException` for every unreadable table so `ScanAsync` has one fallback; the tag is how the caller learns that without reading the scanner.

```csharp
/// <exception cref="MftException">The table is damaged, truncated, too large, or laid out in a way this scanner does not read.</exception>
```

### Form

- One sentence per tag, present tense, no "This method...", no "Gets or sets".
- A short block keeps `/// <summary>...</summary>` on one line, followed by one line per `<param>` and `<returns>`.
- A sentence that passes about 140 columns is split: the tag on its own line, the text wrapped beneath, the closing tag on its own line.
- `<see cref="..."/>` for members, `<paramref name="..."/>` for the method's own parameters, `<c>...</c>` for literals; no bare backticks, which the tooltip shows verbatim.
- The block must be well-formed XML: `&amp;` for `&`, `&lt;` for a literal `<`. (`GenerateDocumentationFile` is off, so the compiler will not tell you.)
- A `<remarks>` holds the one thing that does not fit the summary: a format note, a thread rule, a reference. Not a second summary.
- Comments inside the body stay `//`; a `///` block belongs only in front of a declaration.

## Exceptions

### Catch what you expect

Name the exceptions the operation is known to throw, with `when` to list several.

```csharp
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
```

Not `catch (Exception)` because "something might go wrong". A `catch (Exception)` or a bare `catch` is for the few places where anything at all may fail and the app must carry on regardless (the crash reporter, COM calls into the Explorer shell, the update check, the clipboard), and the comment inside says that.

### Translate at the boundary

Low-level failures become the one exception the caller handles. `MftScanner` throws `MftException` for every way the file table can be unreadable, so `ScanAsync` has one fallback instead of a list.

```csharp
if (ApplyFixups(record0) != RecordStatus.Ok)
{
    throw new MftException("The first MFT record is damaged.");
}
```

### Messages for a person

An exception message is a sentence a user or a log reader can act on: `"$MFT extension record {number} lies outside the extents found so far; this layout is not supported."`, not `"bad extent"`. Messages that reach the UI go through `Strings` like any other text.

### Quiet helpers

When cleanup must not fail the operation, give it a method whose name says so, and keep the reasons inside it.

```csharp
private static void KillQuietly(Process helper)
{
    try
    {
        if (!helper.HasExited)
        {
            helper.Kill();
        }
    }
    catch (InvalidOperationException)
    {
        // The helper exited between the check and the kill, or was never started.
    }
}
```

The call site then reads `using (ct.Register(() => KillQuietly(helper)))` with no `try` in sight.

---

## What a rename must not touch

A change that is "only renames" can still break the app at runtime. Before renaming anything that is not a local, a parameter or a private member, search the whole solution, including `.axaml`, `.resx`, the tests and the installer scripts.

| Surface | How it is referenced | What breaks |
|---|---|---|
| XAML | `Click="Rescan_Click"`, `x:Name="ScanCard"`, `{Binding ...}` | compiles, then fails at runtime or silently does nothing |
| Tests | `internal` members (`UpdateWindow.Abridge`, `MftScanner.Geometry`) and named arguments (`includeHidden: true`, `nowUtc: Now`) | the test project fails to compile |
| Strings | `Strings.Get("Scan_StageRead")`, `Strings.Format(...)` | `LocalizationTests` fails; a missing key shows as English or as the key |
| Settings | property names in `AppSettings` are the `settings.json` keys | the setting resets for every user |
| Saved scans, custom palettes, CSV | `.sscan` layout, `[JsonPropertyName]`, column order | files written by older versions stop loading |
| Command line | `--scene`, `--shot`, `--audit` in `CommandLine.cs` and the PowerShell tools | the scripts break |
| P/Invoke | signatures and struct layouts in `MftScanner`, `ElevatedScan`, `TitleBarTheme`, `ShellProperties` | Windows does not care what the names say; the offsets must match |

Renaming a parameter is safe only after grepping for `name:` uses of it; a named argument in a caller is a compile error you will not see until the caller builds.

---

## Language

Code, comments, commit messages, changelog and UI text are in American English: `color`, `center`, `gray`, `behavior`. No em dashes in comments or text; use a colon, a semicolon, parentheses or a new sentence. The author is `ClearanceClarence` everywhere the project names one.

---

## Checklist

Before a pull request, read the diff once with only these in mind:

- Could a stranger say what every variable holds from its name alone? (`i` is the only one-letter name you write without thinking.)
- Is there a block three levels deep that an early `return` or `continue` would flatten?
- Is there a ternary inside a ternary? A condition with more than three terms without a name?
- Is any method longer than a screen, or doing two things its name does not say?
- Is any expression written twice?
- Is there a body without braces, a `catch` on one line, two declarations on one line?
- Is `var` used where the type is not on the right, or the type repeated where it is?
- Is there a `!` that a local, a pattern or `OfType<T>()` would remove?
- Does every `catch` that swallows say why? Does every method of substance have a one-sentence summary?
- Does every documented method have a `<param>` per parameter in order, a `<returns>` when it returns, a `<typeparam>` when generic? Does each tag say something the name does not?
- Does any comment describe the code as it used to be?
- Did a rename touch XAML, tests, resource keys, settings, file formats or P/Invoke?
