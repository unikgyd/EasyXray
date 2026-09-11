using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using EasyXray.Services;
using Microsoft.Win32;
using QRCoder;
using XrayDeploy.Core;

namespace EasyXray.ViewModels;

public sealed class TopologyViewModel : INotifyPropertyChanged
{
    private readonly IRemoteWorkflow _workflow;
    private readonly KnownHostStore _knownHosts;
    private ServerProfile _server = new() { Name = "New Ubuntu server" };
    private string? _profilePath;
    private InboundProfile? _selectedInbound;
    private OutboundProfile? _selectedOutbound;
    private ExitChoice? _selectedExit;
    private string _status = "第一步：填写服务器 IP、SSH 用户和密钥；新手可直接使用“极速部署 REALITY”。";
    private string _serverHost = "";
    private string _sshUser = "ubuntu";
    private int _sshPort = 22;
    private string _hostFingerprint = "";
    private string _privateKeyPath = "";
    private string _certificateDomain = "";
    private string _certificateEmail = "";
    private bool _isBusy;
    private bool _isXrayActive;
    private string _xrayServiceStatus = "尚未检查 Xray 服务状态。";
    private bool _isStatusError;
    private bool _isCompactLayout;
    private int _currentStep;
    private SecureString? _sshPassword;
    private SecureString? _privateKeyPassphrase;
    private SecureString? _sudoPassword;
    private SecureString? _rootPassword;

    public TopologyViewModel() : this(new RemoteWorkflow(), new KnownHostStore()) { }
    internal TopologyViewModel(IRemoteWorkflow workflow, KnownHostStore? knownHosts = null)
    {
        _workflow = workflow;
        _knownHosts = knownHosts ?? new KnownHostStore();
    }

    public ObservableCollection<InboundProfile> Inbounds { get; } = [];
    public ObservableCollection<OutboundProfile> Outbounds { get; } = [];
    public ObservableCollection<BlockRule> BlockRules { get; } = [];
    public ObservableCollection<ClientAccessItem> ClientLinks { get; } = [];
    public ObservableCollection<ExitChoice> ExitChoices { get; } = [new("Direct", new DirectDestination())];
    public InboundProfile? SelectedInbound { get => _selectedInbound; set { _selectedInbound = value; if (value is Hysteria2InboundProfile hysteria && !string.IsNullOrWhiteSpace(hysteria.CertificateDomain)) { _certificateDomain = hysteria.CertificateDomain; OnPropertyChanged(nameof(CertificateDomain)); } OnPropertyChanged(); OnPropertyChanged(nameof(IsRealitySelected)); OnPropertyChanged(nameof(IsHysteriaSelected)); NotifyRealityEditorChanged(); } }
    public OutboundProfile? SelectedOutbound { get => _selectedOutbound; set { _selectedOutbound = value; OnPropertyChanged(); } }
    public ExitChoice? SelectedExit { get => _selectedExit; set { _selectedExit = value; OnPropertyChanged(); } }
    public string Status { get => _status; private set { _status = value; IsStatusError = LooksLikeError(value); OnPropertyChanged(); } }
    public string ServerHost { get => _serverHost; set { if (!string.Equals(_serverHost, value, StringComparison.OrdinalIgnoreCase)) HostFingerprint = ""; _serverHost = value; OnPropertyChanged(); } }
    public string SshUser { get => _sshUser; set { _sshUser = value; OnPropertyChanged(); } }
    public int SshPort { get => _sshPort; set { if (_sshPort != value) HostFingerprint = ""; _sshPort = value; OnPropertyChanged(); } }
    public string HostFingerprint { get => _hostFingerprint; set { _hostFingerprint = value; OnPropertyChanged(); } }
    public string PrivateKeyPath { get => _privateKeyPath; set { _privateKeyPath = value; OnPropertyChanged(); } }
    public string CertificateDomain { get => _certificateDomain; set { _certificateDomain = value.Trim(); if (SelectedInbound is Hysteria2InboundProfile hysteria) hysteria.CertificateDomain = _certificateDomain; OnPropertyChanged(); } }
    public string CertificateEmail { get => _certificateEmail; set { _certificateEmail = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); } }
    public bool IsXrayActive { get => _isXrayActive; private set { _isXrayActive = value; OnPropertyChanged(); } }
    public string XrayServiceStatus { get => _xrayServiceStatus; private set { _xrayServiceStatus = value; OnPropertyChanged(); } }
    public bool IsStatusError { get => _isStatusError; private set { _isStatusError = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusForeground)); } }
    public string StatusForeground => IsStatusError ? "#FFB4AB" : "#E8F5E9";
    public bool IsCompactLayout { get => _isCompactLayout; private set { _isCompactLayout = value; OnPropertyChanged(); } }
    public int CurrentStep { get => _currentStep; set { _currentStep = Math.Clamp(value, 0, 2); OnPropertyChanged(); } }
    public bool IsRealitySelected => SelectedInbound is RealityInboundProfile;
    public bool IsHysteriaSelected => SelectedInbound is Hysteria2InboundProfile;
    public bool HasClientLinks => ClientLinks.Count > 0;
    public string RealityTarget
    {
        get => (SelectedInbound as RealityInboundProfile)?.Target ?? "";
        set { if (SelectedInbound is RealityInboundProfile profile) { profile.Target = value; OnPropertyChanged(); } }
    }
    public string RealityServerName
    {
        get => (SelectedInbound as RealityInboundProfile)?.ServerNames.FirstOrDefault() ?? "";
        set { if (SelectedInbound is RealityInboundProfile profile) { EnsureFirst(profile.ServerNames); profile.ServerNames[0] = value; OnPropertyChanged(); } }
    }
    public string RealityShortId
    {
        get => (SelectedInbound as RealityInboundProfile)?.ShortIds.FirstOrDefault() ?? "";
        set { if (SelectedInbound is RealityInboundProfile profile) { EnsureFirst(profile.ShortIds); profile.ShortIds[0] = value; OnPropertyChanged(); } }
    }
    public string RealityPublicKey => (SelectedInbound as RealityInboundProfile)?.PublicKey ?? "";

    public ICommand AddRealityInboundCommand => new RelayCommand(_ => AddRealityInbound());
    public ICommand AddHysteriaInboundCommand => new RelayCommand(_ => AddHysteriaInbound());
    public ICommand AddRealityOutboundCommand => new RelayCommand(_ => AddRealityOutbound());
    public ICommand AddHysteriaOutboundCommand => new RelayCommand(_ => AddHysteriaOutbound());
    public ICommand AddWireGuardOutboundCommand => new RelayCommand(_ => AddWireGuardOutbound());
    public ICommand AddStunPresetCommand => new RelayCommand(_ => AddStunPreset());
    public ICommand ApplyExitCommand => new RelayCommand(_ => ApplyExit());
    public ICommand GenerateRandomCommand => new RelayCommand(_ => GenerateRandomValues());
    public ICommand ValidateCommand => new RelayCommand(_ => Validate());
    public ICommand SaveCommand => new AsyncRelayCommand(SaveAsync);
    public ICommand OpenCommand => new AsyncRelayCommand(OpenAsync);
    public ICommand TestConnectionCommand => new AsyncRelayCommand(TestConnectionAsync);
    public ICommand GenerateRealityKeysCommand => new AsyncRelayCommand(GenerateRealityKeysAsync);
    public ICommand IssueCertificateCommand => new AsyncRelayCommand(IssueCertificateAsync);
    public ICommand DeployCommand => new AsyncRelayCommand(DeployAsync);
    public ICommand CheckXrayStatusCommand => new AsyncRelayCommand(CheckXrayStatusAsync);
    public ICommand BrowseKeyCommand => new RelayCommand(_ => BrowseKey());
    public ICommand ImportWireGuardCommand => new RelayCommand(_ => ImportWireGuard());
    public ICommand NextFromServerCommand => new AsyncRelayCommand(NextFromServerAsync);
    public ICommand NextStepCommand => new RelayCommand(_ => CurrentStep++);
    public ICommand PreviousStepCommand => new RelayCommand(_ => CurrentStep--);
    public ICommand OneClickRealityCommand => new AsyncRelayCommand(OneClickRealityAsync);
    public ICommand CopyLinkCommand => new RelayCommand(item => CopyLink(item as ClientAccessItem));
    public ICommand CopyAllLinksCommand => new RelayCommand(_ => CopyAllLinks());
    public ICommand CopyVlessLinksCommand => new RelayCommand(_ => CopyLinks("REALITY", "VLESS"));
    public ICommand CopyHysteria2LinksCommand => new RelayCommand(_ => CopyLinks("Hysteria2", "Hysteria2"));

    public void UpdateLayoutWidth(double width) => IsCompactLayout = width < 1080;

    private void AddRealityInbound()
    {
        var profile = new RealityInboundProfile { Name = "REALITY inbound", Tag = $"reality-{Guid.NewGuid():N}"[..16], ListenPort = 443, PrivateKey = "", PublicKey = "" };
        profile.Users.Add(new(Guid.NewGuid(), "user"));
        RefreshRealityIdentity(profile);
        AddInbound(profile, "已添加 REALITY 入站，部署时会自动生成服务器密钥。");
    }
    private void AddHysteriaInbound()
    {
        var profile = new Hysteria2InboundProfile { Name = "Hysteria2 inbound", Tag = $"hy2-{Guid.NewGuid():N}"[..16], ListenPort = 443, CertificateDomain = CertificateDomain };
        profile.Users.Add(new("user", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))));
        AddInbound(profile, "已添加 Hysteria2 入站；必须填写域名和邮箱以申请可信证书。");
    }
    private void AddRealityOutbound() => AddOutbound(new RealityOutboundProfile { Name = "REALITY next hop", Tag = $"reality-out-{Guid.NewGuid():N}"[..20], ServerAddress = "", ServerPort = 443, UserId = Guid.NewGuid(), ServerName = "", PublicKey = "", ShortId = "" });
    private void AddHysteriaOutbound() => AddOutbound(new Hysteria2OutboundProfile { Name = "Hysteria2 next hop", Tag = $"hy2-out-{Guid.NewGuid():N}"[..16], ServerAddress = "", ServerPort = 443, Authentication = "", TlsServerName = "" });
    private void AddWireGuardOutbound()
    {
        var profile = new WireGuardOutboundProfile { Name = "WireGuard next hop", Tag = $"wg-out-{Guid.NewGuid():N}"[..15], EndpointHost = "", EndpointPort = 51820, PrivateKey = "", PeerPublicKey = "" };
        profile.LocalAddresses.Add("10.0.0.2/32"); profile.AllowedIPs.Add("0.0.0.0/0"); AddOutbound(profile);
    }
    private void AddInbound(InboundProfile profile, string status) { _server.Inbounds.Add(profile); Inbounds.Add(profile); _server.Bindings.Add(new RouteBinding { InboundId = profile.Id, Destination = new DirectDestination() }); SelectedInbound = profile; SelectedExit = ExitChoices[0]; Status = status + " 默认出口为 Direct。"; }
    private void AddOutbound(OutboundProfile profile) { _server.Outbounds.Add(profile); Outbounds.Add(profile); ExitChoices.Add(new(profile.Name, new OutboundDestination(profile.Id))); SelectedOutbound = profile; Status = "已添加一个可选的链式出口。"; }
    private void AddStunPreset() { var rule = PrivacyPresets.CreateCommonStunUdpRule(SelectedInbound?.Id); _server.BlockRules.Add(rule); BlockRules.Add(rule); Status = "已添加可检查的 STUN/WebRTC UDP 阻断规则。"; }
    private void ApplyExit() { if (SelectedInbound is null || SelectedExit is null) { Status = "请先选择一个入站和它的主出口。"; return; } _server.Bindings.RemoveAll(binding => binding.InboundId == SelectedInbound.Id); _server.Bindings.Add(new RouteBinding { InboundId = SelectedInbound.Id, Destination = SelectedExit.Destination }); Status = $"{SelectedInbound.Name} 的出口已设为 {SelectedExit.Name}。"; }
    private void GenerateRandomValues()
    {
        if (SelectedInbound is RealityInboundProfile reality) { RefreshRealityIdentity(reality); RefreshSelectedInbound(); Status = $"已生成新的 UUID、Short ID 和伪装 SNI（{reality.ServerNames[0]}）。"; }
        else if (SelectedInbound is Hysteria2InboundProfile hysteria) { hysteria.Users.Clear(); hysteria.Users.Add(new("user", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)))); RefreshSelectedInbound(); Status = "已生成新的 Hysteria2 认证密码。"; }
        else Status = "请先选择一个入站。";
    }
    private void Validate() { var validation = new RealityTopologyValidator().Validate(_server); Status = validation.IsValid ? "本地拓扑检查通过。" : FormatValidationIssue(validation.Issues[0]); }
    private async Task SaveAsync() { _server.ClientAddress = string.IsNullOrWhiteSpace(ServerHost) ? _server.ClientAddress : ServerHost; var path = _profilePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XrayDeploy", "server-profile.json"); await new ServerProfileStore().SaveAsync(_server, path); _profilePath = path; Status = $"配置已保存：{path}"; }
    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog { Filter = "XrayDeploy profile (*.json)|*.json|All files (*.*)|*.*" }; if (dialog.ShowDialog() != true) return;
        try { _server = await new ServerProfileStore().LoadAsync(dialog.FileName); _profilePath = dialog.FileName; if (!string.IsNullOrWhiteSpace(_server.ClientAddress)) ServerHost = _server.ClientAddress; SyncCollections(); TryBuildClientLinks(); Status = $"配置已载入：{_profilePath}"; } catch (Exception exception) { Status = $"无法载入配置：{exception.Message}"; }
    }
    private async Task TestConnectionAsync() => await RunRemoteAsync(async _ => { await EnsureConnectionAsync(); });
    private async Task GenerateRealityKeysAsync() => await RunRemoteAsync(async _ => { if (SelectedInbound is not RealityInboundProfile profile) { Status = "请先选择一个 REALITY 入站。"; return; } await EnsureConnectionAsync(); var keys = await _workflow.GenerateRealityKeysAsync(ConnectionRequest()); profile.PrivateKey = keys.PrivateKey; profile.PublicKey = keys.PublicKey; RefreshSelectedInbound(); Status = "已在服务器生成新的 REALITY 密钥对。"; });
    private async Task IssueCertificateAsync() => await RunRemoteAsync(async _ => { if (SelectedInbound is not Hysteria2InboundProfile profile) { Status = "请先选择一个 Hysteria2 入站。"; return; } if (!await EnsureConnectionAsync()) return; var certificate = await _workflow.IssueCertificateAsync(ConnectionRequest(), new(CertificateDomain, CertificateEmail)); profile.CertificateDomain = CertificateDomain; profile.TlsCertificatePath = certificate.CertificatePath; profile.TlsPrivateKeyPath = certificate.PrivateKeyPath; RefreshSelectedInbound(); Status = $"{CertificateDomain} 的可信证书已准备好。"; });
    private async Task DeployAsync() => await RunRemoteAsync(async _ => await PrepareAndDeployAsync());
    private async Task CheckXrayStatusAsync() => await RunRemoteAsync(async _ =>
    {
        if (!await EnsureConnectionAsync()) return;
        var health = await _workflow.GetXrayServiceStatusAsync(ConnectionRequest());
        IsXrayActive = health.IsActive;
        XrayServiceStatus = health.Message;
        Status = health.Message;
    });
    private async Task NextFromServerAsync() => await RunRemoteAsync(async _ => { if (await EnsureConnectionAsync()) CurrentStep = 1; });
    private async Task OneClickRealityAsync() => await RunRemoteAsync(async _ =>
    {
        var profile = Inbounds.OfType<RealityInboundProfile>().FirstOrDefault();
        if (profile is null) { AddRealityInbound(); profile = (RealityInboundProfile)SelectedInbound!; }
        SelectedInbound = profile;
        if (!await EnsureConnectionAsync()) return;
        var keys = await _workflow.GenerateRealityKeysAsync(ConnectionRequest());
        profile.PrivateKey = keys.PrivateKey;
        profile.PublicKey = keys.PublicKey;
        await PrepareAndDeployAsync(connectionAlreadyChecked: true);
        CurrentStep = 2;
    });
    private async Task<bool> EnsureConnectionAsync()
    {
        var wasUnpinned = string.IsNullOrWhiteSpace(HostFingerprint);
        if (wasUnpinned)
            HostFingerprint = await _knownHosts.FindAsync(ServerHost, SshPort) ?? "";
        var result = await _workflow.TestConnectionAsync(ConnectionRequest(allowUnpinned: true));
        HostFingerprint = result.HostFingerprint;
        if (wasUnpinned)
            await _knownHosts.PinAsync(ServerHost, SshPort, result.HostFingerprint);
        Status = result.IsSupported ? result.Message : $"步骤 1（服务器）需要处理：{result.Message}";
        return result.IsSupported;
    }

    private async Task PrepareAndDeployAsync(bool connectionAlreadyChecked = false)
    {
        if (!connectionAlreadyChecked && !await EnsureConnectionAsync()) return;
        if (!_server.Inbounds.Any(profile => profile.Enabled)) throw new InvalidOperationException("请至少添加一个入站，推荐选择 REALITY。");
        EnsureDefaultBindings();

        foreach (var reality in _server.Inbounds.OfType<RealityInboundProfile>().Where(profile => profile.Enabled))
            RefreshRealityIdentity(reality);

        foreach (var reality in _server.Inbounds.OfType<RealityInboundProfile>().Where(profile => profile.Enabled && (string.IsNullOrWhiteSpace(profile.PrivateKey) || string.IsNullOrWhiteSpace(profile.PublicKey))))
        {
            var keys = await _workflow.GenerateRealityKeysAsync(ConnectionRequest());
            reality.PrivateKey = keys.PrivateKey;
            reality.PublicKey = keys.PublicKey;
        }

        foreach (var hysteria in _server.Inbounds.OfType<Hysteria2InboundProfile>().Where(profile => profile.Enabled))
        {
            if (string.IsNullOrWhiteSpace(CertificateDomain) || string.IsNullOrWhiteSpace(CertificateEmail))
                throw new InvalidOperationException("安全的 Hysteria2 必须填写 DNS 域名和证书联系邮箱；没有域名时请使用 REALITY。");
            hysteria.CertificateDomain = CertificateDomain;
            if (string.IsNullOrWhiteSpace(hysteria.TlsCertificatePath) || string.IsNullOrWhiteSpace(hysteria.TlsPrivateKeyPath))
            {
                Status = $"正在为 {CertificateDomain} 申请 TLS 证书……";
                var certificate = await _workflow.IssueCertificateAsync(ConnectionRequest(), new(CertificateDomain, CertificateEmail));
                hysteria.TlsCertificatePath = certificate.CertificatePath;
                hysteria.TlsPrivateKeyPath = certificate.PrivateKeyPath;
            }
        }

        var validation = new RealityTopologyValidator().Validate(_server);
        if (!validation.IsValid) { Status = FormatValidationIssue(validation.Issues[0]); return; }
        Status = "正在验证并部署 Xray；失败时会自动回滚……";
        _server.ClientAddress = ServerHost;
        var result = await _workflow.DeployAsync(ConnectionRequest(), _server);
        Status = result.Message;
        if (result.Succeeded)
        {
            BuildClientLinks();
            var health = await _workflow.GetXrayServiceStatusAsync(ConnectionRequest());
            IsXrayActive = health.IsActive;
            XrayServiceStatus = health.Message;
            Status = health.IsActive
                ? $"部署成功，Xray 已激活；已生成 {ClientLinks.Count} 个客户端链接和二维码。"
                : $"配置已部署，但运行检查失败：{health.Message}";
        }
    }

    private void EnsureDefaultBindings()
    {
        foreach (var inbound in _server.Inbounds.Where(profile => profile.Enabled))
            if (!_server.Bindings.Any(binding => binding.Enabled && binding.InboundId == inbound.Id))
                _server.Bindings.Add(new RouteBinding { InboundId = inbound.Id, Destination = new DirectDestination() });
    }

    private void RefreshSelectedInbound() => NotifyRealityEditorChanged();

    private void NotifyRealityEditorChanged()
    {
        OnPropertyChanged(nameof(RealityTarget));
        OnPropertyChanged(nameof(RealityServerName));
        OnPropertyChanged(nameof(RealityShortId));
        OnPropertyChanged(nameof(RealityPublicKey));
    }

    private static void EnsureFirst(IList<string> values)
    {
        if (values.Count == 0) values.Add("");
    }

    private void BuildClientLinks()
    {
        ClientLinks.Clear();
        foreach (var inbound in _server.Inbounds.OfType<RealityInboundProfile>().Where(profile => profile.Enabled))
        foreach (var user in inbound.Users)
        {
            var export = new RealityClientExporter().Export(inbound, user, ServerHost, $"{inbound.Name}-{user.Name}");
            ClientLinks.Add(ClientAccessItem.Create("REALITY", $"{inbound.Name} / {user.Name}", export.VlessUri));
        }

        foreach (var inbound in _server.Inbounds.OfType<Hysteria2InboundProfile>().Where(profile => profile.Enabled))
        foreach (var user in inbound.Users)
        {
            var export = new Hysteria2ClientExporter().Export(inbound, user, inbound.CertificateDomain, $"{inbound.Name}-{user.Name}");
            ClientLinks.Add(ClientAccessItem.Create("Hysteria2", $"{inbound.Name} / {user.Name}", export.Hy2Uri));
        }
        OnPropertyChanged(nameof(HasClientLinks));
    }

    private void TryBuildClientLinks()
    {
        if (string.IsNullOrWhiteSpace(ServerHost)) return;
        try { BuildClientLinks(); }
        catch { ClientLinks.Clear(); OnPropertyChanged(nameof(HasClientLinks)); }
    }

    private void CopyLink(ClientAccessItem? item)
    {
        if (item is null) return;
        try { Clipboard.SetText(item.Link); Status = $"已复制 {item.Name} 的客户端链接。"; }
        catch (Exception exception) { Status = $"无法访问剪贴板：{exception.Message}"; }
    }

    private void CopyAllLinks()
    {
        if (ClientLinks.Count == 0) { Status = "部署成功后才会生成客户端链接。"; return; }
        try { Clipboard.SetText(string.Join(Environment.NewLine, ClientLinks.Select(item => item.Link))); Status = $"已复制全部 {ClientLinks.Count} 个客户端链接。"; }
        catch (Exception exception) { Status = $"无法访问剪贴板：{exception.Message}"; }
    }

    private void CopyLinks(string protocol, string displayName)
    {
        var links = ClientLinks.Where(item => item.Protocol == protocol).Select(item => item.Link).ToArray();
        if (links.Length == 0) { Status = $"当前没有可复制的 {displayName} 链接。"; return; }
        try { Clipboard.SetText(string.Join(Environment.NewLine, links)); Status = $"已复制 {links.Length} 个 {displayName} 链接。"; }
        catch (Exception exception) { Status = $"无法访问剪贴板：{exception.Message}"; }
    }

    private async Task RunRemoteAsync(Func<ServerConnectionRequest, Task> operation)
    {
        try { IsBusy = true; await operation(ConnectionRequest(allowUnpinned: true)); } catch (Exception exception) { Status = $"步骤 {CurrentStep + 1} 操作失败：{exception.Message}"; } finally { IsBusy = false; }
    }
    private ServerConnectionRequest ConnectionRequest(bool allowUnpinned = false)
    {
        if (string.IsNullOrWhiteSpace(ServerHost)) throw new InvalidOperationException("请填写服务器 IP 或主机名。");
        if (!allowUnpinned && string.IsNullOrWhiteSpace(HostFingerprint)) throw new InvalidOperationException("请先检测服务器，让程序固定它的 SSH 主机公钥。");
        if (string.IsNullOrWhiteSpace(PrivateKeyPath) && !HasValue(_sshPassword)) throw new InvalidOperationException("请选择 SSH 私钥，或在高级选项中填写 SSH 密码。");
        return new(ServerHost, SshPort, SshUser, HostFingerprint, string.IsNullOrWhiteSpace(PrivateKeyPath) ? null : PrivateKeyPath,
            ProviderFor(_sshPassword), ProviderFor(_privateKeyPassphrase), ProviderFor(_sudoPassword), ProviderFor(_rootPassword));
    }

    // PasswordBox cannot be safely data-bound. These methods copy its SecureString;
    // none of these values are part of ServerProfile or saved to disk.
    public void SetSshPassword(SecureString value) => SetSecret(ref _sshPassword, value);
    public void SetPrivateKeyPassphrase(SecureString value) => SetSecret(ref _privateKeyPassphrase, value);
    public void SetSudoPassword(SecureString value) => SetSecret(ref _sudoPassword, value);
    public void SetRootPassword(SecureString value) => SetSecret(ref _rootPassword, value);
    private static bool HasValue(SecureString? value) => value is { Length: > 0 };
    private static void SetSecret(ref SecureString? destination, SecureString value)
    {
        destination?.Dispose();
        destination = value.Length == 0 ? null : value.Copy();
    }
    private static Func<CancellationToken, ValueTask<string?>>? ProviderFor(SecureString? value) =>
        HasValue(value) ? cancellationToken => new(Reveal(value!)) : null;
    private static string? Reveal(SecureString value)
    {
        var pointer = Marshal.SecureStringToBSTR(value);
        try { return Marshal.PtrToStringBSTR(pointer); }
        finally { Marshal.ZeroFreeBSTR(pointer); }
    }
    private void BrowseKey() { var dialog = new OpenFileDialog { Filter = "SSH key files (*.pem;*.*)|*.pem;*.*" }; if (dialog.ShowDialog() == true) PrivateKeyPath = dialog.FileName; }
    private void ImportWireGuard()
    {
        var dialog = new OpenFileDialog { Filter = "WireGuard configuration (*.conf)|*.conf|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var name = Path.GetFileNameWithoutExtension(dialog.FileName);
            var profile = new WireGuardConfigImporter().Import(File.ReadAllText(dialog.FileName), name, $"wg-out-{Guid.NewGuid():N}"[..15]);
            AddOutbound(profile);
            Status = $"已导入 WireGuard 出口“{profile.Name}”，请选择入站并应用这个出口。";
        }
        catch (Exception exception)
        {
            Status = $"无法导入 WireGuard 配置：{exception.Message}";
        }
    }
    private void SyncCollections() { EnsureDefaultBindings(); Inbounds.Clear(); foreach (var item in _server.Inbounds) Inbounds.Add(item); Outbounds.Clear(); foreach (var item in _server.Outbounds) Outbounds.Add(item); BlockRules.Clear(); foreach (var item in _server.BlockRules) BlockRules.Add(item); ExitChoices.Clear(); ExitChoices.Add(new("Direct", new DirectDestination())); foreach (var item in _server.Outbounds) ExitChoices.Add(new(item.Name, new OutboundDestination(item.Id))); SelectedInbound = Inbounds.FirstOrDefault(); SelectedExit = ExitChoices[0]; }
    private static void RefreshRealityIdentity(RealityInboundProfile profile)
    {
        var camouflage = RealityCamouflageTargets[RandomNumberGenerator.GetInt32(RealityCamouflageTargets.Length)];
        profile.Users.Clear();
        profile.Users.Add(new RealityUser(Guid.NewGuid(), "user"));
        profile.ShortIds.Clear();
        profile.ShortIds.Add(RandomShortId());
        profile.ServerNames.Clear();
        profile.ServerNames.Add(camouflage.ServerName);
        profile.Target = camouflage.Target;
    }

    private static string FormatValidationIssue(ValidationIssue issue)
    {
        var step = issue.Path.StartsWith("Server", StringComparison.Ordinal) ? "步骤 1（服务器）" :
            issue.Path.Contains("Outbound", StringComparison.Ordinal) || issue.Path.Contains("Binding", StringComparison.Ordinal) || issue.Path.StartsWith("Block", StringComparison.Ordinal) ? "步骤 3（出口）" : "步骤 2（入站）";
        return $"{step} 需要处理：{issue.Path} — {issue.Message}";
    }

    private static bool LooksLikeError(string status) => status.Contains("失败", StringComparison.Ordinal) ||
        status.Contains("无法", StringComparison.Ordinal) || status.Contains("未运行", StringComparison.Ordinal) || status.Contains("未检测", StringComparison.Ordinal) ||
        status.Contains("需要处理", StringComparison.Ordinal) || status.StartsWith("请", StringComparison.Ordinal);

    private static readonly (string Target, string ServerName)[] RealityCamouflageTargets =
    [
        ("www.cloudflare.com:443", "www.cloudflare.com"),
        ("dl.google.com:443", "dl.google.com")
    ];

    private static string RandomShortId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record ExitChoice(string Name, RouteDestination Destination);
public sealed record ClientAccessItem(string Protocol, string Name, string Link, BitmapImage QrCode)
{
    public static ClientAccessItem Create(string protocol, string name, string link)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        var png = qrCode.GetGraphic(7);
        var image = new BitmapImage();
        using var stream = new MemoryStream(png);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return new(protocol, name, link, image);
    }
}
public sealed class RelayCommand(Action<object?> execute) : ICommand { public event EventHandler? CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => execute(parameter); }
public sealed class AsyncRelayCommand(Func<Task> execute) : ICommand { public event EventHandler? CanExecuteChanged { add { } remove { } } public bool CanExecute(object? parameter) => true; public async void Execute(object? parameter) => await execute(); }
