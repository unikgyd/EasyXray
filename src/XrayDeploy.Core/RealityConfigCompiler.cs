using System.Text.Json;
using System.Text.Json.Nodes;

namespace XrayDeploy.Core;

public sealed class RealityConfigCompiler(RealityTopologyValidator? validator = null)
{
    private const string DirectTag = "xraydeploy-direct";
    private readonly RealityTopologyValidator _validator = validator ?? new RealityTopologyValidator();

    public string Compile(ServerProfile server)
    {
        _validator.Validate(server).ThrowIfInvalid();
        var inbounds = new JsonArray();
        var outbounds = new JsonArray
        {
            new JsonObject { ["tag"] = DirectTag, ["protocol"] = "freedom" },
            new JsonObject { ["tag"] = "xraydeploy-block", ["protocol"] = "blackhole" }
        };
        var rules = new JsonArray();

        foreach (var inbound in server.Inbounds.Where(profile => profile.Enabled))
            inbounds.Add(inbound switch
            {
                RealityInboundProfile reality => CompileInbound(reality),
                Hysteria2InboundProfile hysteria => CompileInbound(hysteria),
                _ => throw new InvalidOperationException("Unsupported inbound.")
            });

        foreach (var outbound in server.Outbounds.Where(profile => profile.Enabled))
            outbounds.Add(outbound switch
            {
                RealityOutboundProfile reality => CompileOutbound(reality),
                Hysteria2OutboundProfile hysteria => CompileOutbound(hysteria),
                WireGuardOutboundProfile wireGuard => CompileOutbound(wireGuard),
                _ => throw new InvalidOperationException("Unsupported outbound.")
            });

        foreach (var blockRule in server.BlockRules.Where(rule => rule.Enabled))
        {
            var rule = new JsonObject
            {
                ["type"] = "field",
                ["network"] = blockRule.Network!.Value == NetworkProtocol.Tcp ? "tcp" : "udp",
                ["port"] = string.Join(",", blockRule.Ports)
            };
            if (blockRule.InboundId is not null)
                rule["inboundTag"] = new JsonArray(server.Inbounds.Single(inbound => inbound.Id == blockRule.InboundId.Value).Tag);
            rule["outboundTag"] = "xraydeploy-block";
            rules.Add(rule);
        }

        foreach (var binding in server.Bindings.Where(binding => binding.Enabled))
        {
            var inbound = server.Inbounds.Single(profile => profile.Id == binding.InboundId);
            if (!inbound.Enabled) continue;
            var outboundTag = binding.Destination switch
            {
                DirectDestination => DirectTag,
                OutboundDestination destination => server.Outbounds.Single(profile => profile.Id == destination.OutboundId).Tag,
                _ => throw new InvalidOperationException("Unsupported primary route destination.")
            };
            rules.Add(new JsonObject
            {
                ["type"] = "field",
                ["inboundTag"] = new JsonArray(inbound.Tag),
                ["outboundTag"] = outboundTag
            });
        }

        var config = new JsonObject
        {
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["routing"] = new JsonObject { ["domainStrategy"] = "AsIs", ["rules"] = rules }
        };
        return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject CompileInbound(RealityInboundProfile profile) => new()
    {
        ["tag"] = profile.Tag,
        ["listen"] = profile.ListenAddress,
        ["port"] = profile.ListenPort,
        ["protocol"] = "vless",
        ["settings"] = new JsonObject
        {
            ["decryption"] = "none",
            ["clients"] = new JsonArray(profile.Users.Select(user => new JsonObject
            {
                ["id"] = user.Id.ToString(),
                ["email"] = user.Name,
                ["flow"] = user.Flow
            }).ToArray())
        },
        ["streamSettings"] = new JsonObject
        {
            ["network"] = "tcp",
            ["tcpSettings"] = new JsonObject
            {
                ["acceptProxyProtocol"] = false,
                ["header"] = new JsonObject { ["type"] = "none" }
            },
            ["security"] = "reality",
            ["realitySettings"] = new JsonObject
            {
                ["show"] = false,
                // "target" is the current REALITY field.  Older Xray releases
                // also accepted "dest", but emitting the current name keeps the
                // generated configuration aligned with xray-core examples.
                ["target"] = profile.Target,
                ["xver"] = 0,
                ["serverNames"] = new JsonArray(profile.ServerNames.Select(value => JsonValue.Create(value)).ToArray()),
                ["privateKey"] = profile.PrivateKey,
                ["shortIds"] = new JsonArray(profile.ShortIds.Select(value => JsonValue.Create(value)).ToArray())
            }
        }
    };

    private static JsonObject CompileOutbound(RealityOutboundProfile profile) => new()
    {
        ["tag"] = profile.Tag,
        ["protocol"] = "vless",
        ["settings"] = new JsonObject
        {
            ["vnext"] = new JsonArray(new JsonObject
            {
                ["address"] = profile.ServerAddress,
                ["port"] = profile.ServerPort,
                ["users"] = new JsonArray(new JsonObject
                {
                    ["id"] = profile.UserId.ToString(),
                    ["encryption"] = "none",
                    ["flow"] = profile.Flow
                })
            })
        },
        ["streamSettings"] = new JsonObject
        {
            ["network"] = "tcp",
            ["security"] = "reality",
            ["realitySettings"] = new JsonObject
            {
                ["serverName"] = profile.ServerName,
                ["fingerprint"] = profile.Fingerprint,
                ["publicKey"] = profile.PublicKey,
                ["shortId"] = profile.ShortId,
                ["spiderX"] = ""
            }
        }
    };

    private static JsonObject CompileInbound(Hysteria2InboundProfile profile) => new()
    {
        ["tag"] = profile.Tag,
        ["listen"] = profile.ListenAddress,
        ["port"] = profile.ListenPort,
        ["protocol"] = "hysteria",
        ["settings"] = new JsonObject
        {
            ["version"] = 2,
            ["users"] = new JsonArray(profile.Users.Select(user => new JsonObject
            {
                ["auth"] = user.Authentication,
                ["email"] = user.Name
            }).ToArray())
        },
        ["streamSettings"] = HysteriaInboundStreamSettings(profile)
    };

    private static JsonObject CompileOutbound(Hysteria2OutboundProfile profile) => new()
    {
        ["tag"] = profile.Tag,
        ["protocol"] = "hysteria",
        ["settings"] = new JsonObject { ["version"] = 2, ["address"] = profile.ServerAddress, ["port"] = profile.ServerPort },
        ["streamSettings"] = HysteriaOutboundStreamSettings(profile)
    };

    private static JsonObject CompileOutbound(WireGuardOutboundProfile profile)
    {
        var peer = new JsonObject
        {
            ["publicKey"] = profile.PeerPublicKey,
            ["endpoint"] = $"{profile.EndpointHost}:{profile.EndpointPort}",
            ["allowedIPs"] = new JsonArray(profile.AllowedIPs.Select(value => JsonValue.Create(value)).ToArray())
        };
        if (!string.IsNullOrWhiteSpace(profile.PresharedKey)) peer["preSharedKey"] = profile.PresharedKey;
        if (profile.PersistentKeepalive is not null) peer["keepAlive"] = profile.PersistentKeepalive;
        var settings = new JsonObject
        {
            ["secretKey"] = profile.PrivateKey,
            ["address"] = new JsonArray(profile.LocalAddresses.Select(value => JsonValue.Create(value)).ToArray()),
            ["peers"] = new JsonArray(peer)
        };
        if (profile.Mtu is not null) settings["mtu"] = profile.Mtu;
        return new JsonObject { ["tag"] = profile.Tag, ["protocol"] = "wireguard", ["settings"] = settings };
    }

    private static JsonObject HysteriaInboundStreamSettings(Hysteria2InboundProfile profile)
    {
        var stream = new JsonObject
        {
            ["network"] = "hysteria",
            ["security"] = "tls",
            ["hysteriaSettings"] = new JsonObject
            {
                ["version"] = 2,
                ["udpIdleTimeout"] = profile.UdpIdleTimeoutSeconds
            },
            ["tlsSettings"] = new JsonObject
            {
                ["serverName"] = profile.CertificateDomain,
                ["certificates"] = new JsonArray(new JsonObject
                {
                    ["certificateFile"] = HysteriaTlsPaths.Certificate(profile),
                    ["keyFile"] = HysteriaTlsPaths.PrivateKey(profile)
                }),
                ["alpn"] = new JsonArray("h3")
            }
        };
        AddHysteriaObfuscation(stream, profile.Obfuscation);
        return stream;
    }

    private static JsonObject HysteriaOutboundStreamSettings(Hysteria2OutboundProfile profile)
    {
        var stream = new JsonObject
        {
            ["network"] = "hysteria",
            ["security"] = "tls",
            ["hysteriaSettings"] = new JsonObject
            {
                ["version"] = 2,
                ["auth"] = profile.Authentication,
                ["udpIdleTimeout"] = profile.UdpIdleTimeoutSeconds
            },
            ["tlsSettings"] = new JsonObject
            {
                ["serverName"] = profile.TlsServerName,
                ["allowInsecure"] = profile.AllowInsecureTls,
                ["alpn"] = new JsonArray("h3")
            }
        };
        AddHysteriaObfuscation(stream, profile.Obfuscation);
        return stream;
    }

    private static void AddHysteriaObfuscation(JsonObject stream, string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return;
        stream["finalmask"] = new JsonObject
        {
            ["udp"] = new JsonArray(new JsonObject
            {
                ["type"] = "salamander",
                ["settings"] = new JsonObject { ["password"] = password }
            })
        };
    }
}
