using System;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.IdentityAccess.Application.Security;
using CoreBanking.IdentityAccess.Domain.Security;
using CoreBanking.IdentityAccess.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoreBanking.IdentityAccess.Api.Security;

/// <summary>
/// Registers one synthetic development wrapping key so the protected-field flow is executable locally. The key
/// material lives in the secret provider; only public metadata reaches <c>security.key_metadata</c>. A missing key
/// is reported and envelopes are refused, never silently downgraded.
/// Decision: P1-E02-D13 item D13-E.
/// </summary>
public static class PayloadKeyBootstrap
{
    public const string DevelopmentKeyId = "dev-payload-wrapping";

    public const string InstitutionConfigurationKey = "PayloadEncryption:DevelopmentInstitution";

    public const string DefaultInstitutionValue = "dev-institution";

    public static async Task EnsureAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IPublicKeyMetadataService keys = scope.ServiceProvider.GetRequiredService<IPublicKeyMetadataService>();

        string institutionValue = configuration[InstitutionConfigurationKey]?.Trim() is { Length: > 0 } configured
            ? configured
            : DefaultInstitutionValue;
        InstitutionId institution = InstitutionId.From(institutionValue);

        // Key metadata is tenant-owned, so the scope must carry the institution before any query runs.
        ITenantContext tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenant.Establish(institution);
        try
        {
            PublicKeySetView existing = await keys
                .GetActiveKeysAsync(institution, KeyPurposes.PayloadWrapping, cancellationToken)
                .ConfigureAwait(false);
            if (existing.Keys.Count > 0)
            {
                return;
            }

            PublicKeyView registered = await keys.RegisterVersionAsync(
                institution,
                DevelopmentKeyId,
                KeyPurposes.PayloadWrapping,
                DevelopmentKeyOperationPort.DefaultProviderReference,
                cancellationToken).ConfigureAwait(false);

            StartupLog.DevelopmentPayloadKeyRegistered(logger, registered.KeyId, registered.KeyVersion, institution.Value);
        }
        catch (KeyOperationException)
        {
            StartupLog.DevelopmentPayloadKeyUnavailable(logger);
        }
        catch (DbUpdateException exception)
        {
            StartupLog.DevelopmentPayloadKeyRegistrationFailed(logger, exception);
        }
        finally
        {
            tenant.Clear();
        }
    }
}
