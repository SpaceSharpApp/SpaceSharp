using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpaceSharp.Services;

/// <summary>Who a supporter key was made out to.</summary>
/// <param name="Name">The name the supporter asked to be shown; empty when they gave none.</param>
/// <param name="Order">The order the key belongs to, as the shop numbers it.</param>
/// <param name="Since">The day the key was issued.</param>
public sealed record Supporter(string Name, string Order, DateOnly Since);

/// <summary>Reads supporter keys. A key is a short note (name, order, date) signed by the project's private key, and it is checked
/// here against the public half, so nothing is asked of a server. The key unlocks no feature: it puts the supporter's name in About.</summary>
public static class SupporterKey
{
    /// <summary>What every key starts with; the digit is the format's version.</summary>
    public const string Prefix = "SSUP1";

    /// <summary>The public half of the signing key, as base64 of its SubjectPublicKeyInfo (ECDSA P-256). Written by
    /// tools/supporter/new-keys.ps1; while it is empty no key is accepted.</summary>
    internal const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEv4GWQJL0VRWjwH1yyla9omjXkSxIh6B971vujPpCHFfROHmJ36JB1bQL3njZiXXW+QhBCBsswdikKC6QPCEXeA==";

    /// <summary>The longest text looked at; a real key is about 200 characters.</summary>
    private const int LongestKey = 2000;

    /// <summary>Reads a key against the project's public key.</summary>
    /// <param name="text">The key as pasted; line breaks and spaces an email put in are ignored.</param>
    /// <returns>Who the key was made out to, or null when it is not a key this project signed.</returns>
    public static Supporter? Read(string? text) => Read(text, PublicKey);

    /// <summary>Reads a key against a given public key.</summary>
    /// <param name="text">The key as pasted; line breaks and spaces an email put in are ignored.</param>
    /// <param name="publicKey">Base64 of the SubjectPublicKeyInfo to check the signature with; empty accepts nothing.</param>
    /// <returns>Who the key was made out to, or null when the text is not a key signed by that key's private half.</returns>
    public static Supporter? Read(string? text, string publicKey)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > LongestKey || publicKey.Length == 0)
        {
            return null;
        }

        string key = string.Concat(text.Where(character => !char.IsWhiteSpace(character)));
        string[] parts = key.Split('.');
        if (parts.Length != 3 || parts[0] != Prefix)
        {
            return null;
        }

        try
        {
            // The signature covers the prefix too, so a note signed for another format version does not pass as this one.
            byte[] signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            byte[] signature = Base64Url.DecodeFromChars(parts[2]);
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!verifier.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                return null;
            }
            return ReadNote(Base64Url.DecodeFromChars(parts[1]));
        }
        catch (Exception problem) when (problem is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the signed note: a JSON object with "n" (name), "o" (order) and "d" (date as yyyy-MM-dd).</summary>
    /// <param name="note">The note's UTF-8 bytes, already known to carry a good signature.</param>
    /// <returns>The supporter, or null when the note has no order or no readable date.</returns>
    private static Supporter? ReadNote(byte[] note)
    {
        using var document = JsonDocument.Parse(note);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string name = Text(root, "n");
        string order = Text(root, "o");
        if (order.Length == 0 || !DateOnly.TryParseExact(Text(root, "d"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly since))
        {
            return null;
        }
        return new Supporter(name.Trim(), order, since);
    }

    /// <summary>One text field of the note.</summary>
    /// <param name="note">The note's root object.</param>
    /// <param name="field">The field's name.</param>
    /// <returns>The field's text, or an empty string when it is missing or not text.</returns>
    private static string Text(JsonElement note, string field) =>
        note.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}

/// <summary>The supporters who asked to be named, from the embedded Resources/Supporters.txt. The file is brought up to date by hand
/// before a release, so the list is as of the build.</summary>
public static class SupporterList
{
    /// <summary>The name of the embedded list as the assembly stores it.</summary>
    internal const string ListResource = "SpaceSharp.Resources.Supporters.txt";

    /// <summary>The names, in the order they joined.</summary>
    public static IReadOnlyList<string> Names { get; } = Load();

    /// <summary>Reads names from the list's text: one per line, with blank lines and lines starting with # skipped.</summary>
    /// <param name="text">The whole file.</param>
    /// <returns>The names, trimmed, each once, in file order.</returns>
    public static IReadOnlyList<string> Parse(string text) =>
        text.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Reads the embedded list.</summary>
    /// <returns>The names, or none when the list is not embedded.</returns>
    private static IReadOnlyList<string> Load()
    {
        using Stream? stream = typeof(SupporterList).Assembly.GetManifestResourceStream(ListResource);
        if (stream is null)
        {
            return [];
        }
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }
}
