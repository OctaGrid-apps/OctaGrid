using System.Security.Cryptography;
using OctaGrid.Application.Secrets;

namespace OctaGrid.Application.Tests;

public sealed class AesGcmSecretProtectorTests
{
    private sealed class KeySource : ISecretKeySource
    {
        private readonly byte[] _key;

        public KeySource(byte fill, int size = AesGcmSecretProtector.KeySize)
        {
            _key = Enumerable.Repeat(fill, size).ToArray();
        }

        public byte[] GetOrCreateKey() => _key;
    }

    private static AesGcmSecretProtector Protector(byte fill = 1) => new(new KeySource(fill));

    [Theory]
    [InlineData("")]
    [InlineData("123456")]
    [InlineData("{\"apiKey\":\"t\\\"o'k\\\\en ключ 🔑\"}")]
    public void Protected_text_reads_back_as_the_original(string plaintext)
    {
        var protector = Protector();

        var stored = protector.Protect(plaintext, "printer-1");

        Assert.Equal(plaintext, protector.Unprotect(stored, "printer-1"));
    }

    [Fact]
    public void The_stored_text_does_not_contain_the_secret_and_differs_every_time()
    {
        var protector = Protector();

        var first = protector.Protect("s3cret-value", "printer-1");
        var second = protector.Protect("s3cret-value", "printer-1");

        Assert.DoesNotContain("s3cret-value", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Another_purpose_cannot_read_it()
    {
        var protector = Protector();
        var stored = protector.Protect("123456", "printer-1");

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(stored, "printer-2"));
    }

    [Fact]
    public void Another_key_cannot_read_it()
    {
        var stored = Protector(1).Protect("123456", "printer-1");

        Assert.ThrowsAny<CryptographicException>(() => Protector(2).Unprotect(stored, "printer-1"));
    }

    [Fact]
    public void Changed_text_is_rejected()
    {
        var protector = Protector();
        var bytes = Convert.FromBase64String(protector.Protect("123456", "printer-1"));
        bytes[^1] ^= 0x01;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(Convert.ToBase64String(bytes), "printer-1"));
    }

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void Text_that_was_never_protected_is_rejected(string stored)
    {
        Assert.ThrowsAny<CryptographicException>(() => Protector().Unprotect(stored, "printer-1"));
    }

    [Fact]
    public void A_key_of_the_wrong_size_is_refused()
    {
        var protector = new AesGcmSecretProtector(new KeySource(1, size: 16));

        Assert.ThrowsAny<CryptographicException>(() => protector.Protect("123456", "printer-1"));
    }
}
