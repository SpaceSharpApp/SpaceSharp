<p align="center"><img src="images/export.png" width="900" alt="The Export window: a strip with the format, the columns and the row options over a preview table"></p>

Right-click › More › **Export…** on the map, or **Export…** in the Changes view, opens one window that writes a table of files and folders as CSV or TSV, or puts it on the clipboard.

## What goes in

The picker at the top right chooses the rows:

- **Items in <folder>**: the direct children of the folder you right-clicked (a file's folder for a file).
- **Everything under <folder>**: every file and folder below it.
- **The largest files on the drive**: the top 1,000.
- **The filter's matches**, when a filter is on.
- **What changed since the compared scan**, when one is loaded: biggest change first, at most 5,000 rows.

Rows come largest first. The line under the heading says how many and how much they add up to; for a whole drive it reads "Collecting the rows…" for a moment first, and the file size in the foot fills in a moment after the table.

Everything else sits in one strip over the table: the format, the columns, how many rows, and three row options.

## Format

Three parts of one control; hover over a part for when to pick it.

- **CSV, comma**: what most tools read, and Excel in English.
- **CSV, semicolon**: Excel where the decimal sign is a comma, such as Norway; a comma-separated file opens there as one column.
- **TSV, tab**: pastes straight into a spreadsheet. **Copy** always puts the table on the clipboard as TSV, whatever is picked, since that is what sheets paste as cells.

Files are UTF-8 with a byte-order mark, so Excel reads accented names correctly.

## Columns and rows

The **Columns** button says how many are picked, "6 of 10", and opens the list. Tick the ones you want: Path, Name, Folder, Type, Size (bytes), Size on disk (bytes), Size (as the map writes it), Share of scan (percent, one decimal), Modified (UTC), Change since the compared scan (bytes). The headers in the file are always English, so the same file reads the same everywhere. Under the list, **All** ticks every column and **Default** goes back to the set a first export has.

**At most** writes all, 1,000 or 100 rows. Under **Rows**: **Include folders** off writes files only; **Header row** off starts with the first row; **Bytes with separators** writes 15,562,073,417 instead of 15562073417, which is easier on the eye but stops a spreadsheet from summing the column, so turn it off for numbers you will calculate with.

## Preview

The table shows the first 100 rows as the file will hold them, one column per picked column with the numbers lined up on the right, then a line saying how many rows follow; it changes with every option. The header stays in place while the rows scroll, and a table wider than the window scrolls sideways, so no path is cut short. The foot shows the file name (editable), the extension the format gives it, and the size the file will have. Characters a file name cannot hold, such as the colon in "C:", are left out of the suggested name.

Your choices are kept for next time.
