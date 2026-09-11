namespace XrayDeploy.Core;

public sealed record CertificateRequest(string Domain, string Email);
public sealed record IssuedCertificate(string CertificatePath, string PrivateKeyPath, DateTimeOffset ExpiresAt);

public interface ICertificateProvisioner
{
    Task<IssuedCertificate> IssueAsync(CertificateRequest request, CancellationToken cancellationToken = default);
}
