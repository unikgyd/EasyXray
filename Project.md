# XrayDeploy

A zero-agent Windows deployment and topology manager for lightweight Ubuntu proxy servers.

---

# 0. HARD PROJECT SCOPE

This project intentionally supports a very small and strict set of technologies.

AI Agents MUST NOT expand this scope unless this document is explicitly changed.

## Supported Server OS

```text
Ubuntu ONLY
```

Do NOT implement support for:

```text
Debian
CentOS
Rocky Linux
AlmaLinux
Arch Linux
OpenWrt
Alpine
Fedora
or any other operating system
```

---

# 1. Supported Inbound Protocols

Only TWO inbound protocol families exist:

```text
VLESS + REALITY
Hysteria2
```

Therefore:

```text
InboundProfile
├── RealityInboundProfile
└── Hysteria2InboundProfile
```

Do NOT implement inbound support for:

```text
WireGuard
VMess
Trojan
Shadowsocks
SOCKS
HTTP Proxy
VLESS + WebSocket
VLESS + generic TLS
TUIC
NaiveProxy
or any other protocol
```

WireGuard is specifically NOT an inbound technology in this project.

---

# 2. Supported Chain Outbounds

Configurable chained proxy outbounds support exactly THREE technologies:

```text
VLESS + REALITY
Hysteria2
WireGuard
```

Therefore:

```text
OutboundProfile
├── RealityOutboundProfile
├── Hysteria2OutboundProfile
└── WireGuardOutboundProfile
```

Do NOT implement configurable proxy outbounds for:

```text
VMess
Trojan
Shadowsocks
SOCKS
HTTP Proxy
OpenVPN
TUIC
SSH Tunnel
or any other proxy protocol
```

---

# 3. Built-in Terminal Routes

Two built-in terminal destinations exist:

```text
DIRECT
BLOCK
```

These are NOT configurable proxy nodes.

They are built-in routing destinations.

Conceptually:

```text
Inbound
→ DIRECT
→ Internet
```

or:

```text
Matched Traffic
→ BLOCK
→ Dropped
```

The application must NOT expose Direct or Block as normal remote proxy profiles.

There is no:

```text
Add Direct Outbound
Add Block Server
```

They are intrinsic system destinations.

---

# 4. Purpose of BLOCK

BLOCK exists for narrow privacy and safety-oriented routing cases.

Primary intended uses:

```text
STUN blocking
WebRTC-related UDP blocking
Explicitly forbidden destination ports
Explicitly forbidden traffic categories supported by the project
```

BLOCK must NOT evolve into a general-purpose filtering platform.

Do NOT implement:

```text
Ad blocking engine
GeoIP blocking
GeoSite blocking
Parental filtering
Full firewall UI
Domain category subscriptions
DNS blacklist management
Antivirus filtering
IDS / IPS
```

BLOCK exists only as a terminal routing destination.

---

# 5. Important WebRTC Limitation

Server-side BLOCK rules only affect traffic that actually reaches the managed server.

If a client application sends WebRTC/STUN traffic directly through the local network instead of through the proxy path, the server cannot block traffic it never receives.

Therefore the application must describe server-side WebRTC/STUN blocking as:

```text
Block STUN/WebRTC traffic traversing this proxy path.
```

Do NOT claim:

```text
100% prevents all client WebRTC leaks
```

unless the entire client traffic path is known to be forced through the proxy/VPN/TUN layer.

---

# 6. Final Protocol Matrix

| Role | REALITY | Hysteria2 | WireGuard | Direct | Block |
|---|---:|---:|---:|---:|---:|
| Inbound | YES | YES | NO | NO | NO |
| Configurable Chain Outbound | YES | YES | YES | NO | NO |
| Terminal Route | NO | NO | NO | YES | YES |
| Client Export | YES | YES | NO | NO | NO |

This matrix is a hard architectural constraint.

---

# 7. Core Product Model

The application manages:

```text
Server
Inbound
Outbound
Binding
BlockRule
```

Conceptually:

```text
ServerProfile
├── Inbounds[]
│   ├── Reality
│   └── Hysteria2
│
├── Outbounds[]
│   ├── Reality
│   ├── Hysteria2
│   └── WireGuard
│
├── Bindings[]
│
└── BlockRules[]
```

---

# 8. Core Topology

Example:

```text
Ubuntu Server
│
├── Inbounds
│   ├── Reality-SG-443
│   └── HY2-SG-8443
│
├── Outbounds
│   ├── Reality-PH
│   ├── HY2-JP
│   └── WG-US
│
├── Bindings
│   ├── Reality-SG-443 → Reality-PH
│   └── HY2-SG-8443 → WG-US
│
└── Block Rules
    └── UDP STUN → BLOCK
```

---

# 9. Supported Traffic Paths

The application must support:

```text
Reality In
→ DIRECT
```

```text
Reality In
→ Reality Out
```

```text
Reality In
→ Hysteria2 Out
```

```text
Reality In
→ WireGuard Out
```

```text
Hysteria2 In
→ DIRECT
```

```text
Hysteria2 In
→ Reality Out
```

```text
Hysteria2 In
→ Hysteria2 Out
```

```text
Hysteria2 In
→ WireGuard Out
```

And selected traffic may terminate at:

```text
→ BLOCK
```

---

# 10. Routing Philosophy

The project is NOT a general-purpose Xray routing editor.

The primary abstraction remains:

```text
Inbound
→ Primary Exit
```

Each inbound has one primary exit:

```text
DIRECT
```

or one configured outbound:

```text
REALITY
Hysteria2
WireGuard
```

BLOCK rules are evaluated separately for explicitly blocked traffic.

---

# 11. Routing Evaluation Model

Conceptually:

```text
Traffic enters Inbound
        │
        ▼
Check Block Rules
        │
        ├── Match
        │    └── BLOCK
        │
        └── No Match
             │
             ▼
        Inbound Binding
             │
             ├── DIRECT
             └── Configured Outbound
```

This ordering is intentional.

Explicit block rules override normal inbound-to-outbound routing.

---

# 12. RouteBinding

Recommended model:

```csharp
public sealed class RouteBinding
{
    public Guid Id { get; init; }

    public Guid InboundId { get; init; }

    public RouteDestination Destination { get; init; }

    public bool Enabled { get; set; } = true;
}
```

Destination:

```csharp
public abstract record RouteDestination;

public sealed record DirectDestination()
    : RouteDestination;

public sealed record OutboundDestination(Guid OutboundId)
    : RouteDestination;
```

BLOCK is not normally used as an inbound's primary destination.

BLOCK is primarily selected through BlockRule.

---

# 13. BlockRule

Create a deliberately limited blocking model.

Suggested abstraction:

```csharp
public sealed class BlockRule
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    public bool Enabled { get; set; } = true;

    public Guid? InboundId { get; set; }

    public NetworkProtocol? Network { get; set; }

    public List<PortRange> Ports { get; set; } = [];
}
```

Keep the first version narrow.

Do NOT implement arbitrary Xray routing JSON input.

---

# 14. Initial Block Rule Capabilities

Initial supported match dimensions:

```text
Inbound
Network protocol
Destination port / port range
```

Example:

```text
Name:
Block STUN

Inbound:
All

Network:
UDP

Ports:
3478
```

Another example:

```text
Block UDP 3478-3481
```

The exact default STUN/WebRTC presets should be configurable and documented rather than presented as mathematically complete WebRTC protection.

---

# 15. WebRTC / STUN Preset

The GUI may eventually provide:

```text
Privacy

[ ] Block common STUN/WebRTC UDP traffic
```

Internally this creates normal BlockRule objects.

Do NOT hide magic routing behavior inside the UI.

The preset should simply populate explicit blocking rules.

Users must be able to inspect what was generated.

---

# 16. No Arbitrary Domain Routing Initially

BLOCK must NOT introduce:

```text
GeoIP
GeoSite
Domain keyword matching
Regex domains
Country routing
ASN routing
Application routing
Process routing
Advertising lists
```

The first version stays focused on:

```text
protocol
port
inbound
```

This is sufficient for the project's initial privacy-oriented blocking requirement.

---

# 17. Inbound Model

```csharp
public abstract class InboundProfile
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    public required string Tag { get; init; }

    public required int ListenPort { get; set; }

    public bool Enabled { get; set; } = true;
}
```

Only:

```text
RealityInboundProfile
Hysteria2InboundProfile
```

may derive from this class.

---

# 18. Outbound Model

```csharp
public abstract class OutboundProfile
{
    public Guid Id { get; init; }

    public required string Name { get; set; }

    public required string Tag { get; init; }

    public bool Enabled { get; set; } = true;
}
```

Only:

```text
RealityOutboundProfile
Hysteria2OutboundProfile
WireGuardOutboundProfile
```

may derive from this class.

Direct and Block must NOT derive from OutboundProfile.

---

# 19. Reality Inbound

Required logical fields:

```text
Name
Tag
Listen Address
Listen Port
Users
UUID
Flow
Target
ServerName
PrivateKey
PublicKey
ShortId
```

Default flow:

```text
xtls-rprx-vision
```

Keep the UI concise.

---

# 20. Reality Outbound

Required client-side fields:

```text
Name
Tag
Server Address
Server Port
UUID
Flow
ServerName
PublicKey
ShortId
Fingerprint
```

PrivateKey is NOT part of the Reality outbound profile.

---

# 21. Hysteria2 Inbound

Logical fields:

```text
Name
Tag
Listen Address
Listen Port
Authentication
TLS Certificate
TLS Private Key
Optional Obfuscation
Necessary performance settings
```

Avoid exposing every advanced option initially.

---

# 22. Hysteria2 Outbound

Logical fields:

```text
Name
Tag
Server Address
Server Port
Authentication
TLS ServerName
TLS verification settings
Optional Obfuscation
Necessary connection settings
```

---

# 23. WireGuard Outbound

WireGuard exists ONLY as an outbound path.

Suggested logical model:

```csharp
public sealed class WireGuardOutboundProfile : OutboundProfile
{
    public required string EndpointHost { get; set; }

    public int EndpointPort { get; set; }

    public required string PrivateKey { get; set; }

    public required string PeerPublicKey { get; set; }

    public string? PresharedKey { get; set; }

    public List<string> LocalAddresses { get; set; } = [];

    public List<string> AllowedIPs { get; set; } = [];

    public int? Mtu { get; set; }

    public int? PersistentKeepalive { get; set; }
}
```

The exact implementation may be adjusted to match the selected runtime approach.

---

# 24. WireGuard Scope

WireGuard supports:

```text
Reality In
→ WireGuard Out
```

and:

```text
HY2 In
→ WireGuard Out
```

Do NOT implement:

```text
WireGuard inbound
WireGuard server provisioning
WireGuard peer server management
WireGuard user QR generation
General WireGuard VPN manager
```

---

# 25. WireGuard Import

Support importing a standard WireGuard client configuration.

Example:

```text
[Interface]
PrivateKey = ...
Address = ...

[Peer]
PublicKey = ...
Endpoint = ...
AllowedIPs = ...
PersistentKeepalive = ...
```

Parse this into:

```text
WireGuardOutboundProfile
```

Do not retain unrelated fields unless needed.

---

# 26. Multiple Inbounds / Outbounds

A single Ubuntu server may contain:

```text
INBOUNDS

Reality :443
Reality :8443
HY2 :2053
```

and:

```text
OUTBOUNDS

Reality-PH
HY2-JP
WG-US
```

Bindings:

```text
Reality :443
→ Reality-PH

Reality :8443
→ WG-US

HY2 :2053
→ HY2-JP
```

Plus:

```text
Block Rules

UDP 3478
→ BLOCK
```

This is a core requirement.

---

# 27. Add Inbound UI

Only display:

```text
+ Add Inbound

REALITY
Hysteria2
```

No third option.

---

# 28. Add Outbound UI

Only display:

```text
+ Add Outbound

REALITY
Hysteria2
WireGuard
```

Do not show Direct or Block here.

---

# 29. Exit Selector

Each inbound has:

```text
Exit
```

Example:

```text
Direct
────────────
PH Reality
JP Hysteria2
US WireGuard
```

BLOCK should not normally appear here.

Blocking belongs under:

```text
Privacy / Block Rules
```

---

# 30. Privacy / Block Rules UI

Later GUI concept:

```text
PRIVACY / BLOCK RULES

[✓] Block common STUN/WebRTC UDP traffic

Custom Rules

Block UDP 3478
All Inbounds

Block UDP 50000-50100
Reality-443

[+ Add Rule]
```

Keep this screen intentionally small.

---

# 31. Chain Proxy Meaning

For this project, chain proxy means:

```text
Inbound
↓
ONE configured outbound
↓
Next network hop
```

Do NOT implement arbitrary local graph chains such as:

```text
Inbound
→ Outbound A
→ Outbound B
→ Outbound C
```

Multi-hop infrastructure should be composed across managed servers.

---

# 32. Multi-Server Chain Example

```text
Client
↓
Server A Reality
↓
Server B Hysteria2
↓
Server C WireGuard Exit
↓
Internet
```

Internally:

```text
Server A
Reality In
→ HY2 Out to Server B
```

```text
Server B
HY2 In
→ WG Out
```

Each server only handles its immediate next hop.

---

# 33. Import From Managed Server

Future convenience feature:

```text
Add Outbound
→ REALITY
→ Import from Managed Server
```

or:

```text
Add Outbound
→ Hysteria2
→ Import from Managed Server
```

Select an existing managed inbound and convert its exported client profile into an outbound profile.

---

# 34. Client Export

XrayDeploy is NOT a proxy client.

It only exports inbound connection information.

REALITY:

```text
VLESS URI
QR Code
Structured Profile
```

Hysteria2:

```text
HY2 URI
QR Code
Structured Profile
```

WireGuard does not have an inbound role in this project.

---

# 35. Operating System

Ubuntu only.

Detection:

```bash
cat /etc/os-release
```

Require:

```text
ID=ubuntu
```

Otherwise stop deployment.

Supported CPU architectures initially:

```text
x86_64
aarch64
```

No distribution abstraction layer is required.

---

# 36. SSH / Privilege Architecture

Before protocol implementation, complete:

```text
SSH
↓
Privilege Resolver
↓
RemoteRootContext
```

Support:

```text
root login
passwordless sudo
sudo with password
su fallback
```

---

# 37. Privilege Resolution

Priority:

```text
id -u
│
├── 0
│   └── RootShell
│
└── non-zero
    ↓
sudo exists?
    ↓
sudo -n true
    │
    ├── yes
    │   └── PasswordlessSudoShell
    │
    └── no
        ↓
interactive sudo
        │
        ├── success
        │   └── PasswordSudoShell
        │
        └── failure
            ↓
            su
            │
            ├── success
            │   └── SuShell
            │
            └── failure
                └── PrivilegeUnavailable
```

---

# 38. Credential Separation

These are independent:

```text
SSH Password
sudo Password
root Password
SSH Key Passphrase
```

Never assume they are equal.

Never store them as plaintext.

---

# 39. RemoteRootContext

```csharp
public interface IRemoteRootContext
{
    IRemoteShell UserShell { get; }

    IPrivilegedShell RootShell { get; }

    IRemoteFileStager FileStager { get; }
}
```

Every deployment component depends on this abstraction.

---

# 40. Remote File Staging

Never upload directly into root-owned paths.

Required:

```text
Windows
↓
SFTP
↓
/tmp/xraydeploy-<random>/
↓
RootShell
↓
Final Path
```

Example:

```text
/tmp/xraydeploy-abcd/config.json
↓
sudo/root install
↓
/etc/xray/config.json
```

---

# 41. Agentless Requirement

The managed server must NOT run:

```text
XrayDeploy agent
Web UI
Database
Management daemon
Node.js backend
Persistent control API
Docker solely for management
```

When the Windows application closes:

```text
XrayDeploy management RAM usage on server = 0
```

Only required network/proxy services remain.

---

# 42. Solution Structure

```text
XrayDeploy.sln

src/
├── XrayDeploy.Core
├── XrayDeploy.Infrastructure
├── XrayDeploy.Cli
└── XrayDeploy.App

tests/
└── XrayDeploy.Tests
```

Recommended technology:

```text
C#
.NET
SSH.NET
System.Text.Json
WPF
MVVM
```

CLI first.

GUI later.

---

# 43. Source of Truth

Windows-side models are authoritative:

```text
ServerProfile
├── Inbounds
├── Outbounds
├── Bindings
└── BlockRules
```

Deployment compiler:

```text
ServerProfile
↓
Validate
↓
Compile
↓
Generate Service Configurations
↓
Deploy
```

UI controls must never directly mutate remote configuration files.

---

# 44. Deployment Pipeline

Every deployment:

```text
Validate local model
↓
Compile config
↓
Stage files
↓
Remote validation
↓
Backup current config
↓
Install new config
↓
Restart affected services
↓
Health check
↓
Success
```

On failure:

```text
Rollback
↓
Restart previous services
↓
Verify recovery
```

---

# 45. Port Validation

Multiple inbounds are supported.

Detect conflicting listener definitions before deployment.

Do not silently deploy obvious port conflicts.

---

# 46. Outbound Validation

Reality outbound must have all required REALITY client values.

Hysteria2 outbound must have all required HY2 client values.

WireGuard outbound must validate at minimum:

```text
Endpoint
Private Key
Peer Public Key
Local Address
AllowedIPs
```

and any fields required by the chosen implementation.

---

# 47. Binding Validation

Every enabled inbound must have exactly one normal exit:

```text
DIRECT
```

or:

```text
REALITY outbound
Hysteria2 outbound
WireGuard outbound
```

Validate:

```text
Outbound exists
Outbound enabled
No deleted outbound referenced
No duplicate conflicting bindings
No obvious self-loop
```

---

# 48. Block Rule Validation

Validate:

```text
Valid network protocol
Valid port
Valid port range
Referenced inbound exists
No impossible range
```

Rules must compile deterministically.

Do not allow arbitrary raw routing JSON in the initial version.

---

# 49. No General Routing Engine

Do NOT implement:

```text
GeoIP
GeoSite
Country routing
Domain routing
ASN routing
Load balancing
Weighted routing
Fallback groups
Random exits
Per-user routing
Application routing
Routing scripting language
```

Normal routing remains:

```text
Inbound
→ DIRECT / REALITY / HY2 / WG
```

with optional explicit:

```text
BlockRule
→ BLOCK
```

---

# 50. Development Phases

## Phase 1 — Remote Infrastructure

Implement:

```text
SSH Transport
Host Key Verification
Interactive SSH
Root Detection
Passwordless sudo
Password sudo
su fallback
Privilege Verification
RemoteRootContext
RemoteFileStager
PosixShellEscaper
Ubuntu Detection
CLI privilege-test
```

Do NOT implement proxy protocols yet.

---

## Phase 2 — REALITY

Implement:

```text
RealityInboundProfile
RealityOutboundProfile
REALITY configuration compiler
Key generation
VLESS export
Service installation
Remote config validation
Atomic deployment
```

Test:

```text
Reality In
→ DIRECT
```

Then:

```text
Reality In
→ Reality Out
```

---

## Phase 3 — Hysteria2

Implement:

```text
Hysteria2InboundProfile
Hysteria2OutboundProfile
HY2 config compiler
Installation
TLS handling
Service management
Client export
Atomic deployment
```

Test:

```text
HY2 In
→ DIRECT
```

Then:

```text
Reality In
→ HY2 Out
```

and:

```text
HY2 In
→ Reality Out
```

---

## Phase 4 — WireGuard Outbound

Implement:

```text
WireGuardOutboundProfile
WireGuard client config import
WireGuard outbound deployment
Routing integration
Connectivity verification
```

Test:

```text
Reality In
→ WG Out
```

and:

```text
HY2 In
→ WG Out
```

Do NOT implement WG inbound/server management.

---

## Phase 5 — BLOCK

Implement built-in BLOCK support.

Start only with:

```text
Network protocol match
Port / port range match
Optional inbound match
```

Test:

```text
UDP 3478
→ BLOCK
```

Then test a Privacy preset that generates explicit BlockRule models.

Do NOT implement general traffic filtering.

---

## Phase 6 — Multiple Inbounds / Outbounds

Test:

```text
Reality :443
→ Reality-PH

Reality :8443
→ WG-US

HY2 :2053
→ HY2-JP
```

while block rules apply independently.

---

## Phase 7 — Multi-Server Import

Support:

```text
Managed Reality Inbound
→ Reality Outbound on another server
```

and:

```text
Managed HY2 Inbound
→ HY2 Outbound on another server
```

---

## Phase 8 — WPF GUI

Only after CLI behavior is stable.

Use:

```text
WPF
MVVM
```

No SSH, privilege, deployment or routing logic inside ViewModels.

---

# 51. Forbidden Scope

AI Agents MUST NOT implement:

```text
Non-Ubuntu operating systems

WireGuard inbound

VMess
Trojan
Shadowsocks
SOCKS
HTTP Proxy
OpenVPN
TUIC
NaiveProxy

Generic protocol plugin systems

Subscription server
Billing
Quota
Expiry
Accounts

Web management panel
Server-side management agent
Management database

GeoIP routing
GeoSite routing
General firewall
Ad blocking system
Arbitrary routing engine
Arbitrary same-server multi-hop graphs
```

---

# 52. Mandatory Protocol Boundary

Every AI Agent must follow:

```text
INBOUND

REALITY
Hysteria2


CHAIN OUTBOUND

REALITY
Hysteria2
WireGuard


BUILT-IN TERMINAL ROUTES

DIRECT
BLOCK
```

No other protocol or route type may be introduced.

---

# 53. Core Product Identity

XrayDeploy is:

> A Windows-native, agentless Ubuntu proxy topology manager focused exclusively on REALITY, Hysteria2 and WireGuard outbound chaining, with minimal explicit BLOCK routing for privacy-oriented traffic control.

Core characteristics:

```text
Ubuntu only

REALITY inbound
Hysteria2 inbound

REALITY outbound
Hysteria2 outbound
WireGuard outbound

DIRECT terminal exit
BLOCK terminal exit

Multiple inbounds
Multiple outbounds
Inbound → outbound binding

Minimal STUN/WebRTC blocking capability

No Web Panel
No Agent
No Database
Low RAM
SSH-based management
```

---

# 54. First AI Agent Instruction

Start ONLY with Phase 1.

Implement:

```text
SSH
PrivilegeResolver
RootShell
PasswordlessSudoShell
PasswordSudoShell
SuShell
RemoteRootContext
RemoteFileStager
Ubuntu validation
CLI privilege-test
```

Do NOT yet implement:

```text
REALITY
Hysteria2
WireGuard
BLOCK
WPF
```

until RemoteRootContext is reliable.

Run:

```text
dotnet build
dotnet test
```

before proceeding.

---

# 55. FINAL RULE

Whenever an AI Agent wants to add functionality, re-read this:

```text
Ubuntu only.

INBOUND:
REALITY
Hysteria2

OUTBOUND:
REALITY
Hysteria2
WireGuard

TERMINAL ROUTES:
DIRECT
BLOCK

BLOCK:
Minimal explicit privacy-oriented blocking only.

Multiple inbounds.
Multiple outbounds.
Inbound → outbound binding.

No generic routing engine.
No protocol expansion.
No Linux distro expansion.
```

The narrow scope is intentional.