using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using SpaceSharp.Services;

namespace SpaceSharp.Tests;

/// <summary>A supporter key is read only when the project's private key signed it, however an email wrapped it.</summary>
public class SupporterKeyTests
{
    // Made by tools/supporter/server/hook.php with a key pair that was thrown away afterwards, so these prove the server's
    // keys read here: PHP writes the signature in DER and the note as UTF-8 JSON.
    private const string ServerPublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEFabC5swN4t4vCRV1NGSuXSER9nCQ8ahKphO9CvW02XwTBQBa9cbwRj3ynu8Cr7fTDdNW9AY4J6/Ywk1V52kB9w==";
    private const string ServerKey = "SSUP1.eyJuIjoiw4VzZSDDmGRlZ8OlcmQgKFNraWVuKSIsIm8iOiIwYTFiMmMzZC0xMTExLTIyMjItMzMzMy00NDQ0NTU1NTY2NjYiLCJkIjoiMjAyNi0xMS0yNSJ9.MEUCIQClvhqJy4dA1NXDp1j5RFNjqmDtfoSopRSV0jbTTu68GwIgEt4FkJznY60A-zyggYBSKzVGObDpDx3DZLufJur5Fk0";
    private const string ServerKeyWithoutName = "SSUP1.eyJuIjoiIiwibyI6IjBhMWIyYzNkLTk5OTktMjIyMi0zMzMzLTQ0NDQ1NTU1NjY2NiIsImQiOiIyMDI2LTExLTI1In0.MEYCIQDw3yGCMDKvsMQKBBCOdxrC69T0DhGLvGObSoEbH_C5pwIhAPtyMFb7tqWtYr4_xwcH60TwW9Ktut_zMxlwFGmM521l";

    private static readonly ECDsa Signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly string PublicKey = Convert.ToBase64String(Signer.ExportSubjectPublicKeyInfo());

    private static string Sign(string note, string prefix = SupporterKey.Prefix)
    {
        string signed = $"{prefix}.{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(note))}";
        byte[] signature = Signer.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return $"{signed}.{Base64Url.EncodeToString(signature)}";
    }

    private const string Note = """{"n":"Ada","o":"order-1","d":"2026-11-25"}""";

    [Fact]
    public void AKeyFromTheServerIsRead()
    {
        Supporter? supporter = SupporterKey.Read(ServerKey, ServerPublicKey);

        Assert.Equal(new Supporter("Åse Ødegård (Skien)", "0a1b2c3d-1111-2222-3333-444455556666", new DateOnly(2026, 11, 25)), supporter);
    }

    [Fact]
    public void AKeyWithoutANameIsStillAKey()
    {
        Assert.Equal(string.Empty, SupporterKey.Read(ServerKeyWithoutName, ServerPublicKey)?.Name);
    }

    [Fact]
    public void LineBreaksAndSpacesFromAnEmailAreIgnored()
    {
        string wrapped = "  " + string.Join("\r\n", ServerKey.Chunk(64).Select(line => new string(line))) + "\r\n";

        Assert.NotNull(SupporterKey.Read(wrapped, ServerPublicKey));
    }

    [Fact]
    public void AKeySignedHereIsRead()
    {
        Assert.Equal(new Supporter("Ada", "order-1", new DateOnly(2026, 11, 25)), SupporterKey.Read(Sign(Note), PublicKey));
    }

    [Fact]
    public void AnotherSignersKeyIsRefused()
    {
        Assert.Null(SupporterKey.Read(Sign(Note), ServerPublicKey));
    }

    [Fact]
    public void AChangedNameIsRefused()
    {
        string[] parts = Sign(Note).Split('.');
        string forged = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(Note.Replace("Ada", "Eve")));

        Assert.Null(SupporterKey.Read($"{parts[0]}.{forged}.{parts[2]}", PublicKey));
    }

    [Fact]
    public void AnotherFormatVersionIsRefused()
    {
        string other = Sign(Note, "SSUP2");

        Assert.Null(SupporterKey.Read(other, PublicKey));
        Assert.Null(SupporterKey.Read(SupporterKey.Prefix + other[5..], PublicKey));
    }

    [Theory]
    [InlineData("""{"n":"Ada","d":"2026-11-25"}""")]
    [InlineData("""{"n":"Ada","o":"order-1","d":"25.11.2026"}""")]
    [InlineData("""{"n":"Ada","o":"order-1"}""")]
    [InlineData("""["Ada"]""")]
    [InlineData("not json")]
    public void ASignedNoteThatIsNotASupporterIsRefused(string note)
    {
        Assert.Null(SupporterKey.Read(Sign(note), PublicKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SSUP1")]
    [InlineData("SSUP1.e30.AAAA")]
    [InlineData("SSUP1.***.???")]
    [InlineData("hello.there.friend")]
    public void AnythingElseIsRefused(string? text)
    {
        Assert.Null(SupporterKey.Read(text, PublicKey));
    }

    [Fact]
    public void WithoutAPublicKeyNothingIsAccepted()
    {
        Assert.Null(SupporterKey.Read(Sign(Note), string.Empty));
        Assert.Null(SupporterKey.Read(Sign(Note), "not a key"));
    }

    /// <summary>A release without the public key would refuse every key anyone bought.</summary>
    [Fact]
    public void TheProjectsPublicKeyIsInPlace()
    {
        Assert.True(SupporterKey.PublicKey.Length > 0, "SupporterKey.PublicKey is empty. Run tools/supporter/new-keys.ps1 once; it makes the key pair and writes the public half into SupporterKey.cs.");

        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(SupporterKey.PublicKey), out _);
        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void TheListSkipsCommentsBlanksAndRepeats()
    {
        IReadOnlyList<string> names = SupporterList.Parse("# heading\r\nAda\r\n\r\n  Grace Hopper  \r\nAda\r\n#Eve\r\n");

        Assert.Equal(["Ada", "Grace Hopper"], names);
    }

    [Fact]
    public void TheListIsEmbedded()
    {
        Assert.NotNull(typeof(SupporterList).Assembly.GetManifestResourceStream(SupporterList.ListResource));
        Assert.DoesNotContain(SupporterList.Names, name => name.StartsWith('#'));
    }
}
