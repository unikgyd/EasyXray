using System.Security.Cryptography;
using XrayDeploy.Infrastructure;

namespace XrayDeploy.Tests;

public sealed class SshHostKeyVerifierTests
{
    [Fact]
    public void FormatsFingerprintForTrustOnFirstUse()
    {
        var key = "server-key"u8.ToArray();

        var fingerprint = SshHostKeyVerifier.Format(key);

        Assert.StartsWith("SHA256:", fingerprint);
        Assert.DoesNotContain('=', fingerprint);
        Assert.True(new SshHostKeyVerifier(fingerprint).IsMatch(key));
    }
    [Fact]
    public void AcceptsOpenSshSha256FingerprintsWithoutBase64Padding()
    {
        var hostKey = RandomNumberGenerator.GetBytes(32);
        var fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

        var verifier = new SshHostKeyVerifier(fingerprint);

        Assert.True(verifier.IsMatch(hostKey));
        Assert.False(verifier.IsMatch(RandomNumberGenerator.GetBytes(32)));
    }
}
