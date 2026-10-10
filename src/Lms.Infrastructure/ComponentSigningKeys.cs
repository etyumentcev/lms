using System.Security.Cryptography;

namespace Lms.Infrastructure;

// In-process synthetic key lifecycle. Trusted host owns RSA handles and must keep them
// alive while trusted here. No private-key export, serialization, remote lookup or fallback.
// Rotation retains old verification trust until explicitly removed. Removal is terminal
// for this ring instance; a stale caller cannot silently reintroduce a retired key.
public sealed class ComponentSigningKeys
{
    private readonly object sync = new();
    private readonly Dictionary<string, RSA> trusted = new(StringComparer.Ordinal);
    private readonly HashSet<string> removed = new(StringComparer.Ordinal);
    private string? active;
    public static string KeyId(RSA key) => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    public string Rotate(RSA key)
    {
        if (key.KeySize < 2048) throw new ArgumentException("RSA key must be at least 2048 bits");
        var id = KeyId(key);
        lock (sync)
        {
            if (removed.Contains(id)) throw new InvalidOperationException("Key trust was terminated");
            // Prove private signing capability without exporting private material.
            var probe = new byte[] { 0x41, 0x32 };
            var signature = key.SignData(probe, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!key.VerifyData(probe, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new CryptographicException("Invalid signing key");
            trusted[id] = key; active = id;
            return id;
        }
    }
    // A validator can hold only a public RSA handle; trusting it never enables signing.
    public string TrustVerificationKey(RSA key)
    {
        if (key.KeySize < 2048) throw new ArgumentException("RSA key must be at least 2048 bits");
        var id = KeyId(key);
        lock (sync)
        {
            if (removed.Contains(id)) throw new InvalidOperationException("Key trust was terminated");
            trusted.TryAdd(id, key);
            return id;
        }
    }
    public void StopSigning() { lock (sync) active = null; }
    public void RemoveTrust(string id)
    {
        lock (sync) { trusted.Remove(id); removed.Add(id); if (active == id) active = null; }
    }
    internal string? ActiveId { get { lock (sync) return active; } }
    internal byte[]? Sign(string id, byte[] data)
    {
        lock (sync)
        {
            if (active != id || !trusted.TryGetValue(id, out var key)) return null;
            try { return KeyId(key) == id ? key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) : null; }
            catch (Exception e) when (e is CryptographicException or ObjectDisposedException) { return null; }
        }
    }
    internal bool Verify(string id, byte[] data, byte[] signature)
    {
        lock (sync)
        {
            if (!trusted.TryGetValue(id, out var key)) return false;
            try { return KeyId(key) == id && key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); }
            catch (Exception e) when (e is CryptographicException or ObjectDisposedException) { return false; }
        }
    }
}
