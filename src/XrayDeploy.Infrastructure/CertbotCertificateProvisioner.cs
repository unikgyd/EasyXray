using System.Text.RegularExpressions;
using XrayDeploy.Core;

namespace XrayDeploy.Infrastructure;

public sealed class CertbotCertificateProvisioner(IRemoteRootContext context) : ICertificateProvisioner
{
    private static readonly Regex DomainPattern = new("^(?=.{1,253}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\\.)+[a-zA-Z]{2,63}$", RegexOptions.CultureInvariant);

    public async Task<IssuedCertificate> IssueAsync(CertificateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!DomainPattern.IsMatch(request.Domain)) throw new ArgumentException("A valid DNS domain is required for Certbot.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@', StringComparison.Ordinal)) throw new ArgumentException("A contact email is required for Certbot.", nameof(request));

        var installed = await context.RootShell.ExecuteAsync("command -v certbot >/dev/null 2>&1", cancellationToken);
        if (!installed.Succeeded)
        {
            var install = await context.RootShell.ExecuteAsync("apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y certbot", cancellationToken);
            if (!install.Succeeded) throw new InvalidOperationException($"Certbot installation failed: {install.StandardError}");
        }

        var issue = await context.RootShell.ExecuteAsync(
            $"certbot certonly --standalone --non-interactive --agree-tos --email {PosixShellEscaper.Quote(request.Email)} -d {PosixShellEscaper.Quote(request.Domain)} --keep-until-expiring",
            cancellationToken);
        if (!issue.Succeeded) throw new InvalidOperationException($"Certbot could not issue the certificate: {issue.StandardError}");

        var certificatePath = $"/etc/letsencrypt/live/{request.Domain}/fullchain.pem";
        var privateKeyPath = $"/etc/letsencrypt/live/{request.Domain}/privkey.pem";
        var exists = await context.RootShell.ExecuteAsync($"test -r {PosixShellEscaper.Quote(certificatePath)} && test -r {PosixShellEscaper.Quote(privateKeyPath)}", cancellationToken);
        if (!exists.Succeeded) throw new InvalidOperationException("Certbot reported success but the certificate files are not readable.");
        return new(certificatePath, privateKeyPath, DateTimeOffset.UtcNow.AddDays(90));
    }
}
