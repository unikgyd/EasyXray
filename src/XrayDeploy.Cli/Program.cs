using XrayDeploy.Core;
using XrayDeploy.Infrastructure;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("""
        XrayDeploy CLI

        privilege-test --host <host> --user <user> --fingerprint <SHA256:base64> [--port 22] [--key <path>]
        validate --profile <server-profile.json>
        compile --profile <server-profile.json> --output <xray-config.json>
        export-reality --profile <server-profile.json> --inbound <tag> --user <name> --server <address>
        export-hysteria2 --profile <server-profile.json> --inbound <tag> --user <name> --server <address>
        import-wireguard --profile <server-profile.json> --config <wireguard.conf> --name <name> --tag <tag>
        deploy --profile <server-profile.json> --host <host> --user <user> --fingerprint <SHA256:base64> [--port 22] [--key <path>]
        reality-keygen --host <host> --user <user> --fingerprint <SHA256:base64> [--port 22] [--key <path>]
        """);
    return 0;
}

if (args[0] is "validate" or "compile" or "export-reality" or "export-hysteria2" or "import-wireguard")
{
    try
    {
        var options = SimpleOptions.Parse(args[1..]);
        var profile = await new ServerProfileStore().LoadAsync(options.Required("--profile"));
        if (args[0] == "import-wireguard")
        {
            var outbound = new WireGuardConfigImporter().Import(
                await File.ReadAllTextAsync(options.Required("--config")),
                options.Required("--name"),
                options.Required("--tag"));
            if (profile.Outbounds.Any(item => item.Tag.Equals(outbound.Tag, StringComparison.Ordinal)))
                throw new ArgumentException("An outbound with this tag already exists.");
            profile.Outbounds.Add(outbound);
            await new ServerProfileStore().SaveAsync(profile, options.Required("--profile"));
            Console.WriteLine($"WireGuard outbound '{outbound.Name}' imported. Select it as an inbound's primary exit before deployment.");
            return 0;
        }
        if (args[0] == "validate")
        {
            var validation = new RealityTopologyValidator().Validate(profile);
            if (validation.IsValid) { Console.WriteLine("Profile is valid."); return 0; }
            foreach (var issue in validation.Issues) Console.Error.WriteLine($"{issue.Path}: {issue.Message}");
            return 3;
        }
        if (args[0] == "compile")
        {
            var output = options.Required("--output");
            await File.WriteAllTextAsync(output, new RealityConfigCompiler().Compile(profile));
            Console.WriteLine($"Xray configuration written to {Path.GetFullPath(output)}.");
            return 0;
        }
        var inbound = profile.Inbounds.SingleOrDefault(item => item.Tag == options.Required("--inbound"));
        var userName = options.Required("--user");
        var server = options.Required("--server");
        if (args[0] == "export-reality" && inbound is RealityInboundProfile reality)
        {
            Console.WriteLine(new RealityClientExporter().Export(reality, reality.Users.Single(user => user.Name == userName), server).VlessUri);
            return 0;
        }
        if (args[0] == "export-hysteria2" && inbound is Hysteria2InboundProfile hysteria)
        {
            Console.WriteLine(new Hysteria2ClientExporter().Export(hysteria, hysteria.Users.Single(user => user.Name == userName), server).Hy2Uri);
            return 0;
        }
        throw new ArgumentException("The selected inbound does not match the requested export protocol.");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Command failed: {exception.Message}");
        return 1;
    }
}

if (args[0] is "deploy" or "reality-keygen")
{
    try
    {
        var options = CommandOptions.Parse(args[1..]);
        var credentials = new ConsoleCredentialProvider();
        var settings = new SshConnectionSettings(options.Host, options.Port, options.User, options.Fingerprint,
            options.KeyPath is null ? credentials.GetSshPasswordAsync : null, options.KeyPath,
            options.KeyPath is null ? null : credentials.GetKeyPassphraseAsync);
        await using var session = new SshNetRemoteSession(settings);
        await session.ConnectAsync();
        var ubuntu = await new UbuntuValidator().ValidateAsync(session);
        if (!ubuntu.IsSupported) { Console.Error.WriteLine($"Server rejected: {ubuntu.FailureReason}"); return 3; }
        var context = await new RemoteRootContextFactory(credentials).CreateAsync(session);
        var installer = new XrayInstaller(context);
        if (args[0] == "reality-keygen")
        {
            var keyPair = await new RemoteRealityKeyGenerator(context, installer).GenerateAsync();
            Console.WriteLine($"Private key: {keyPair.PrivateKey}\nPublic key: {keyPair.PublicKey}");
            return 0;
        }
        var profile = await new ServerProfileStore().LoadAsync(SimpleOptions.Value(args[1..], "--profile"));
        var result = await new RealityRemoteDeploymentService(context, installer).DeployAsync(profile);
        Console.WriteLine(result.Message);
        return result.Succeeded ? 0 : 5;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Remote command failed: {exception.Message}");
        return 1;
    }
}

if (!string.Equals(args[0], "privilege-test", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unknown command. Run with --help for usage.");
    return 2;
}

try
{
    var options = CommandOptions.Parse(args[1..]);
    var credentials = new ConsoleCredentialProvider();
    var settings = new SshConnectionSettings(
        options.Host, options.Port, options.User, options.Fingerprint,
        options.KeyPath is null ? credentials.GetSshPasswordAsync : null,
        options.KeyPath,
        options.KeyPath is null ? null : credentials.GetKeyPassphraseAsync);

    await using var session = new SshNetRemoteSession(settings);
    await session.ConnectAsync();
    Console.WriteLine("SSH connection established; the configured host key was verified.");

    var ubuntu = await new UbuntuValidator().ValidateAsync(session);
    if (!ubuntu.IsSupported)
    {
        Console.Error.WriteLine($"Server rejected: {ubuntu.FailureReason}");
        return 3;
    }
    Console.WriteLine($"Ubuntu validation passed ({ubuntu.Architecture}).");

    var resolution = await new PrivilegeResolver(credentials).ResolveAsync(session);
    if (!resolution.IsAvailable)
    {
        Console.Error.WriteLine($"Privilege test failed: {resolution.FailureReason}");
        return 4;
    }

    var verification = await resolution.RootShell!.ExecuteAsync("id -u");
    if (!verification.Succeeded || verification.StandardOutput.Trim() != "0")
    {
        Console.Error.WriteLine("Privilege test failed: the selected shell did not return uid 0.");
        return 4;
    }

    Console.WriteLine($"Privilege test passed using {resolution.Method}.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Privilege test failed: {exception.Message}");
    return 1;
}

internal sealed record CommandOptions(string Host, int Port, string User, string Fingerprint, string? KeyPath)
{
    public static CommandOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Options must be supplied as --name value pairs.");
            values.Add(args[index], args[index + 1]);
        }

        static string Required(IReadOnlyDictionary<string, string> input, string name) =>
            input.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException($"Missing required option {name}.");

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--host", "--port", "--user", "--fingerprint", "--key", "--profile" };
        if (values.Keys.Any(key => !allowed.Contains(key))) throw new ArgumentException("An unsupported option was supplied.");
        var port = values.TryGetValue("--port", out var rawPort) ? int.Parse(rawPort) : 22;
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "SSH port must be between 1 and 65535.");
        return new(Required(values, "--host"), port, Required(values, "--user"), Required(values, "--fingerprint"), values.GetValueOrDefault("--key"));
    }
}

internal sealed class ConsoleCredentialProvider : IPrivilegeCredentialProvider
{
    public ValueTask<string?> GetSshPasswordAsync(CancellationToken cancellationToken = default) => new(Prompt("SSH password"));
    public ValueTask<string?> GetKeyPassphraseAsync(CancellationToken cancellationToken = default) => new(Prompt("SSH key passphrase (leave blank for none)"));
    public ValueTask<string?> GetSudoPasswordAsync(CancellationToken cancellationToken = default) => new(Prompt("sudo password (leave blank to skip sudo)"));
    public ValueTask<string?> GetRootPasswordAsync(CancellationToken cancellationToken = default) => new(Prompt("root password for su fallback (leave blank to stop)"));

    private static string? Prompt(string label)
    {
        Console.Write(label + ": ");
        var buffer = new List<char>();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && buffer.Count > 0) { buffer.RemoveAt(buffer.Count - 1); continue; }
            if (!char.IsControl(key.KeyChar)) buffer.Add(key.KeyChar);
        }
        Console.WriteLine();
        return buffer.Count == 0 ? null : new string(buffer.ToArray());
    }
}

internal sealed class SimpleOptions(IReadOnlyDictionary<string, string> values)
{
    public string Required(string name) => values.GetValueOrDefault(name) is { Length: > 0 } value ? value : throw new ArgumentException($"Missing required option {name}.");

    public static SimpleOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length) throw new ArgumentException("Options must be supplied as --name value pairs.");
            values.Add(args[index], args[index + 1]);
        }
        return new(values);
    }

    public static string Value(string[] args, string name) => Parse(args).Required(name);
}
