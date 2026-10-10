using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Lms.Infrastructure.Tests;

public sealed class ComponentKeyContractTests
{
    [Fact]
    public void Weak_or_public_only_key_cannot_become_signer()
    {
        var keys = new ComponentSigningKeys(); using var weak = RSA.Create(1024);
        Assert.Throws<ArgumentException>(() => keys.Rotate(weak));
        using var privateKey = RSA.Create(2048); using var publicKey = RSA.Create();
        publicKey.ImportSubjectPublicKeyInfo(privateKey.ExportSubjectPublicKeyInfo(), out _);
        Assert.ThrowsAny<CryptographicException>(() => keys.Rotate(publicKey));
        Assert.Equal("{}", JsonSerializer.Serialize(keys));
        Assert.DoesNotContain("PRIVATE", keys.ToString());
    }
}
