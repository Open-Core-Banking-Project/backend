using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Security;
using Microsoft.Extensions.Configuration;

namespace CoreBanking.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Reads secret material from process environment variables or configuration so local runs and tests are
/// executable without a secret store. This is the development provider ADR-0011 item 2 permits: it is not a
/// production store, it never invents a default value, and a missing secret returns <see langword="null"/> so the
/// caller fails closed.
/// Decision: ADR-0011 items 2 and 3; P1-E02-D13.
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    public const string EnvironmentVariablePrefix = "CB_SECRET_";

    public const string ConfigurationSection = "Secrets";

    private readonly IConfiguration _configuration;

    public EnvironmentSecretProvider(IConfiguration configuration) =>
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    public Task<string?> TryReadAsync(string logicalName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        cancellationToken.ThrowIfCancellationRequested();

        string environmentName = EnvironmentVariablePrefix
            + logicalName.Replace('-', '_').Replace('.', '_').Replace(':', '_').ToUpperInvariant();

        string? value = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult<string?>(value);
        }

        return Task.FromResult(_configuration[$"{ConfigurationSection}:{logicalName}"]);
    }
}
