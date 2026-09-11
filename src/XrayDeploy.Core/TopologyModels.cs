using System.Text.Json.Serialization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace XrayDeploy.Core;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed class ServerProfile : ObservableModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    private string _name = "";
    private string? _clientAddress;
    public required string Name { get => _name; set => SetProperty(ref _name, value); }
    public string? ClientAddress { get => _clientAddress; set => SetProperty(ref _clientAddress, value); }
    public List<InboundProfile> Inbounds { get; set; } = [];
    public List<OutboundProfile> Outbounds { get; set; } = [];
    public List<RouteBinding> Bindings { get; set; } = [];
    public List<BlockRule> BlockRules { get; set; } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RealityInboundProfile), "reality")]
[JsonDerivedType(typeof(Hysteria2InboundProfile), "hysteria2")]
public abstract class InboundProfile : ObservableModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    private string _name = "";
    private string _tag = "";
    private int _listenPort;
    private bool _enabled = true;
    public required string Name { get => _name; set => SetProperty(ref _name, value); }
    public required string Tag { get => _tag; set => SetProperty(ref _tag, value); }
    public required int ListenPort { get => _listenPort; set => SetProperty(ref _listenPort, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(RealityOutboundProfile), "reality")]
[JsonDerivedType(typeof(Hysteria2OutboundProfile), "hysteria2")]
[JsonDerivedType(typeof(WireGuardOutboundProfile), "wireguard")]
public abstract class OutboundProfile : ObservableModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    private string _name = "";
    private string _tag = "";
    private bool _enabled = true;
    public required string Name { get => _name; set => SetProperty(ref _name, value); }
    public required string Tag { get => _tag; set => SetProperty(ref _tag, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
}

public sealed class RealityInboundProfile : InboundProfile
{
    private string _listenAddress = "0.0.0.0";
    private string _target = "www.cloudflare.com:443";
    private string _privateKey = "";
    private string _publicKey = "";
    public string ListenAddress { get => _listenAddress; set => SetProperty(ref _listenAddress, value); }
    public ObservableCollection<RealityUser> Users { get; set; } = [];
    public string Target { get => _target; set => SetProperty(ref _target, value); }
    public ObservableCollection<string> ServerNames { get; set; } = [];
    public required string PrivateKey { get => _privateKey; set => SetProperty(ref _privateKey, value); }
    public required string PublicKey { get => _publicKey; set => SetProperty(ref _publicKey, value); }
    public ObservableCollection<string> ShortIds { get; set; } = [];
}

/// <summary>
/// A REALITY client identity.  This is deliberately mutable: the desktop editor
/// binds directly to these fields, while the compiler still receives the same
/// wire representation as before.
/// </summary>
public sealed class RealityUser : ObservableModel
{
    public RealityUser(Guid id, string name, string flow = "xtls-rprx-vision")
        => (Id, Name, Flow) = (id, name, flow);

    private Guid _id;
    private string _name = "";
    private string _flow = "xtls-rprx-vision";
    public Guid Id { get => _id; set => SetProperty(ref _id, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Flow { get => _flow; set => SetProperty(ref _flow, value); }
}

public sealed class RealityOutboundProfile : OutboundProfile
{
    private string _serverAddress = ""; private int _serverPort; private Guid _userId; private string _flow = "xtls-rprx-vision"; private string _serverName = ""; private string _publicKey = ""; private string _shortId = ""; private string _fingerprint = "chrome";
    public required string ServerAddress { get => _serverAddress; set => SetProperty(ref _serverAddress, value); }
    public required int ServerPort { get => _serverPort; set => SetProperty(ref _serverPort, value); }
    public required Guid UserId { get => _userId; set => SetProperty(ref _userId, value); }
    public string Flow { get => _flow; set => SetProperty(ref _flow, value); }
    public required string ServerName { get => _serverName; set => SetProperty(ref _serverName, value); }
    public required string PublicKey { get => _publicKey; set => SetProperty(ref _publicKey, value); }
    public required string ShortId { get => _shortId; set => SetProperty(ref _shortId, value); }
    public string Fingerprint { get => _fingerprint; set => SetProperty(ref _fingerprint, value); }
}

public sealed class Hysteria2InboundProfile : InboundProfile
{
    private string _listenAddress = "0.0.0.0"; private string _certificateDomain = ""; private string? _tlsCertificatePath; private string? _tlsPrivateKeyPath; private string? _tlsCertificatePem; private string? _tlsPrivateKeyPem; private string? _tlsCertificateLocalPath; private string? _tlsPrivateKeyLocalPath; private string? _obfuscation; private int _udpIdleTimeoutSeconds = 60;
    public string ListenAddress { get => _listenAddress; set => SetProperty(ref _listenAddress, value); }
    public ObservableCollection<Hysteria2User> Users { get; set; } = [];
    public string CertificateDomain { get => _certificateDomain; set => SetProperty(ref _certificateDomain, value); }
    public string? TlsCertificatePath { get => _tlsCertificatePath; set => SetProperty(ref _tlsCertificatePath, value); }
    public string? TlsPrivateKeyPath { get => _tlsPrivateKeyPath; set => SetProperty(ref _tlsPrivateKeyPath, value); }
    public string? TlsCertificatePem { get => _tlsCertificatePem; set => SetProperty(ref _tlsCertificatePem, value); }
    public string? TlsPrivateKeyPem { get => _tlsPrivateKeyPem; set => SetProperty(ref _tlsPrivateKeyPem, value); }
    public string? TlsCertificateLocalPath { get => _tlsCertificateLocalPath; set => SetProperty(ref _tlsCertificateLocalPath, value); }
    public string? TlsPrivateKeyLocalPath { get => _tlsPrivateKeyLocalPath; set => SetProperty(ref _tlsPrivateKeyLocalPath, value); }
    public string? Obfuscation { get => _obfuscation; set => SetProperty(ref _obfuscation, value); }
    public int UdpIdleTimeoutSeconds { get => _udpIdleTimeoutSeconds; set => SetProperty(ref _udpIdleTimeoutSeconds, value); }
}

public static class HysteriaTlsPaths
{
    public static string Certificate(Hysteria2InboundProfile profile) => profile.TlsCertificatePath ?? $"/usr/local/etc/xray/certs/{profile.Id:N}.crt";
    public static string PrivateKey(Hysteria2InboundProfile profile) => profile.TlsPrivateKeyPath ?? $"/usr/local/etc/xray/certs/{profile.Id:N}.key";
}

/// <summary>Mutable for editing the Hysteria2 credential in the WPF form.</summary>
public sealed class Hysteria2User : ObservableModel
{
    public Hysteria2User(string name, string authentication)
        => (Name, Authentication) = (name, authentication);

    private string _name = "";
    private string _authentication = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Authentication { get => _authentication; set => SetProperty(ref _authentication, value); }
}

public sealed class Hysteria2OutboundProfile : OutboundProfile
{
    private string _serverAddress = ""; private int _serverPort; private string _authentication = ""; private string _tlsServerName = ""; private bool _allowInsecureTls; private string? _obfuscation; private int _udpIdleTimeoutSeconds = 60;
    public required string ServerAddress { get => _serverAddress; set => SetProperty(ref _serverAddress, value); }
    public required int ServerPort { get => _serverPort; set => SetProperty(ref _serverPort, value); }
    public required string Authentication { get => _authentication; set => SetProperty(ref _authentication, value); }
    public required string TlsServerName { get => _tlsServerName; set => SetProperty(ref _tlsServerName, value); }
    public bool AllowInsecureTls { get => _allowInsecureTls; set => SetProperty(ref _allowInsecureTls, value); }
    public string? Obfuscation { get => _obfuscation; set => SetProperty(ref _obfuscation, value); }
    public int UdpIdleTimeoutSeconds { get => _udpIdleTimeoutSeconds; set => SetProperty(ref _udpIdleTimeoutSeconds, value); }
}

public sealed class WireGuardOutboundProfile : OutboundProfile
{
    private string _endpointHost = ""; private int _endpointPort; private string _privateKey = ""; private string _peerPublicKey = ""; private string? _presharedKey; private int? _mtu; private int? _persistentKeepalive;
    public required string EndpointHost { get => _endpointHost; set => SetProperty(ref _endpointHost, value); }
    public required int EndpointPort { get => _endpointPort; set => SetProperty(ref _endpointPort, value); }
    public required string PrivateKey { get => _privateKey; set => SetProperty(ref _privateKey, value); }
    public required string PeerPublicKey { get => _peerPublicKey; set => SetProperty(ref _peerPublicKey, value); }
    public string? PresharedKey { get => _presharedKey; set => SetProperty(ref _presharedKey, value); }
    public ObservableCollection<string> LocalAddresses { get; set; } = [];
    public ObservableCollection<string> AllowedIPs { get; set; } = [];
    public int? Mtu { get => _mtu; set => SetProperty(ref _mtu, value); }
    public int? PersistentKeepalive { get => _persistentKeepalive; set => SetProperty(ref _persistentKeepalive, value); }
}

public sealed class RouteBinding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid InboundId { get; init; }
    public required RouteDestination Destination { get; init; }
    public bool Enabled { get; set; } = true;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(DirectDestination), "direct")]
[JsonDerivedType(typeof(OutboundDestination), "outbound")]
public abstract record RouteDestination;
public sealed record DirectDestination : RouteDestination;
public sealed record OutboundDestination(Guid OutboundId) : RouteDestination;

public enum NetworkProtocol { Tcp, Udp }

public readonly record struct PortRange(int Start, int End)
{
    public override string ToString() => Start == End ? Start.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{Start}-{End}";
}

public sealed class BlockRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool Enabled { get; set; } = true;
    public Guid? InboundId { get; set; }
    public NetworkProtocol? Network { get; set; }
    public List<PortRange> Ports { get; set; } = [];
}
