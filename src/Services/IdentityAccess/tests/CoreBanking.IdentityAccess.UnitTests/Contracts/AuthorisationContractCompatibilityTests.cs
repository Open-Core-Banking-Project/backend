using System;
using System.Linq;
using System.Text.Json;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Contracts;

[Trait("Category", "Unit")]
public sealed class AuthorisationContractCompatibilityTests
{
    [Fact]
    public void Actor_contract_uses_explicit_stable_actor_and_assurance_values()
    {
        ActorContextV1 actor = new()
        {
            ActorId = "actor-001",
            ActorType = ActorTypeV1.Staff,
            InstitutionId = "institution-001",
            SessionId = "session-001",
            EntitlementVersion = 9,
            AuthenticationStrength = AuthenticationStrengthV1.MultiFactor,
            ValidatedContext = new[] { "managed_device" },
        };

        using JsonDocument document = JsonDocument.Parse(EventContract.Serialize(actor));

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("staff", document.RootElement.GetProperty("actorType").GetString());
        Assert.Equal("multi_factor", document.RootElement.GetProperty("authenticationStrength").GetString());
        Assert.False(document.RootElement.TryGetProperty("roles", out _));
        Assert.False(document.RootElement.TryGetProperty("permissions", out _));
    }

    [Fact]
    public void Baseline_actor_payload_remains_deserializable_when_a_consumer_adds_an_unknown_field()
    {
        const string payload = """
            {
              "schemaVersion": 1,
              "actorId": "actor-001",
              "actorType": "administrator",
              "institutionId": "institution-001",
              "sessionId": "session-001",
              "entitlementVersion": 12,
              "authenticationStrength": "step_up",
              "validatedContext": ["managed_device"],
              "futureAdditiveField": "ignored-by-v1"
            }
            """;

        ActorContextV1 restored = Assert.IsType<ActorContextV1>(
            EventContract.Deserialize<ActorContextV1>(payload));

        Assert.Equal("actor-001", restored.ActorId);
        Assert.Equal(12, restored.EntitlementVersion);
        Assert.Equal(AuthenticationStrengthV1.StepUp, restored.AuthenticationStrength);
    }

    [Fact]
    public void Entitlement_snapshot_is_tenant_scoped_complete_and_data_minimised()
    {
        EntitlementSnapshotV1 snapshot = new()
        {
            ActorId = "actor-001",
            InstitutionId = "institution-001",
            Version = 14,
            IsComplete = true,
            CapturedAt = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero),
            Grants = new[]
            {
                new PermissionGrantV1
                {
                    Permission = "payment.transfer.initiate",
                    Scope = new GrantScopeV1
                    {
                        InstitutionId = "institution-001",
                        BranchId = "branch-001",
                        Currency = "NGN",
                        MaximumAmount = 125000.50m,
                        MinimumAuthenticationStrength = AuthenticationStrengthV1.MultiFactor,
                    },
                    ValidTo = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
                },
            },
        };

        string json = EventContract.Serialize(snapshot);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement scope = root.GetProperty("grants")[0].GetProperty("scope");

        Assert.Equal("institution-001", root.GetProperty("institutionId").GetString());
        Assert.True(root.GetProperty("isComplete").GetBoolean());
        Assert.Equal(125000.50m, scope.GetProperty("maximumAmount").GetDecimal());
        Assert.DoesNotContain("role", root.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Snapshot_request_requires_an_observed_minimum_version_and_institution()
    {
        EntitlementSnapshotRequestV1 request = new()
        {
            ActorId = "actor-001",
            InstitutionId = "institution-001",
            MinimumVersion = 8,
        };

        string json = EventContract.Serialize(request);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("institution-001", document.RootElement.GetProperty("institutionId").GetString());
        Assert.Equal(8, document.RootElement.GetProperty("minimumVersion").GetInt64());
    }

    [Fact]
    public void Decision_contract_uses_stable_safe_codes_without_resource_existence_detail()
    {
        AuthorisationDecisionV1 decision = new()
        {
            Decision = AuthorisationOutcomeV1.Denied,
            Permission = "payment.transfer.initiate",
            ReasonCode = "auth.scope_mismatch",
            SafeDetail = "The request was refused for the supplied scope.",
            EvaluatedEntitlementVersion = 18,
        };

        AuthorisationDecisionV1 restored = Assert.IsType<AuthorisationDecisionV1>(
            EventContract.Deserialize<AuthorisationDecisionV1>(EventContract.Serialize(decision)));

        Assert.Equal("denied", restored.Decision);
        Assert.Equal("auth.scope_mismatch", restored.ReasonCode);
        Assert.DoesNotContain("account", restored.SafeDetail!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Entitlement_invalidation_contract_carries_a_monotonic_transition_and_no_grants()
    {
        EntitlementVersionAdvancedV1 advanced = new()
        {
            ActorId = "actor-001",
            InstitutionId = "institution-001",
            PreviousVersion = 20,
            NewVersion = 21,
            ChangeReasonCode = "grant_revoked",
            SourceReference = "grant-001",
        };

        string json = EventContract.Serialize(advanced);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("entitlement_version_advanced", IdentityAccessContractV1.EntitlementVersionAdvancedEventType);
        Assert.Equal(20, document.RootElement.GetProperty("previousVersion").GetInt64());
        Assert.Equal(21, document.RootElement.GetProperty("newVersion").GetInt64());
        Assert.False(document.RootElement.TryGetProperty("grants", out _));
        Assert.False(document.RootElement.TryGetProperty("permissions", out _));
    }

    [Fact]
    public void Contract_assembly_does_not_reference_internal_application_domain_or_infrastructure_assemblies()
    {
        string[] references = typeof(ActorContextV1).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("CoreBanking.IdentityAccess.Application", references);
        Assert.DoesNotContain("CoreBanking.IdentityAccess.Domain", references);
        Assert.DoesNotContain("CoreBanking.IdentityAccess.Infrastructure", references);
        Assert.DoesNotContain("CoreBanking.BuildingBlocks.Application", references);
        Assert.DoesNotContain("CoreBanking.BuildingBlocks.Domain", references);
    }
}
