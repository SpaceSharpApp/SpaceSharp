namespace SpaceSharp.Util;

/// <summary>Shortens a name from the middle, so its start and its end, where a file's extension is, both stay readable.</summary>
public static class MiddleEllipsis
{
    /// <summary>The mark that stands for what was left out.</summary>
    public const string Mark = "…";

    /// <summary>Shortens a text until it fits, keeping as much of it as will fit around one <see cref="Mark"/>.</summary>
    /// <param name="text">The text, such as a file name.</param>
    /// <param name="fits">Says whether a candidate fits where it is going; a shorter candidate must fit whenever a longer one does.</param>
    /// <returns>
    /// The text itself when it fits; otherwise the most of its start and end that fit with the mark between them, the end getting no
    /// more than the start; the mark alone when not even one character fits beside it.
    /// </returns>
    public static string Shorten(string text, Func<string, bool> fits)
    {
        if (text.Length == 0 || fits(text))
        {
            return text;
        }

        // Search for the most characters that can be kept: every count below the answer fits and every count above it does not.
        int fewest = 0;
        int most = text.Length - 1;
        while (fewest < most)
        {
            int kept = (fewest + most + 1) / 2;
            if (fits(Keep(text, kept)))
            {
                fewest = kept;
            }
            else
            {
                most = kept - 1;
            }
        }
        return Keep(text, fewest);
    }

    /// <summary>The text with all but a number of characters replaced by the mark.</summary>
    /// <param name="text">The whole text.</param>
    /// <param name="kept">How many characters stay, split between the start and the end; an odd one goes to the start.</param>
    /// <returns>The start, the mark and the end.</returns>
    private static string Keep(string text, int kept)
    {
        int end = kept / 2;
        int start = kept - end;
        return string.Concat(text.AsSpan(0, start), Mark, text.AsSpan(text.Length - end));
    }
}
