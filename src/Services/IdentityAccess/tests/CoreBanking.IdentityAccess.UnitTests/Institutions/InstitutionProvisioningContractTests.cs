using System.Text.Json;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.IdentityAccess.Contracts;
using CoreBanking.IdentityAccess.Infrastructure.Institutions;
using Xunit;

namespace CoreBanking.IdentityAccess.UnitTests.Institutions;

[Trait("Category", "Unit")]
public sealed class InstitutionProvisioningContractTests
{
    [Fact]
    public void Consumer_subscribes_to_the_versioned_core_ledger_institution_topic()
    {
        Assert.Equal(
            "corebanking.core-ledger.institution.v1",
            InstitutionProvisioningConsumer.TopicName);
    }

    [Fact]
    public void Acknowledgement_carries_plan_service_and_causation_references()
    {
        InstitutionBoundaryInitialisedV1 acknowledgement = new(
            "institution-001",
            "identity-access",
            3,
            "provisioning-event-001");

        string json = EventContract.Serialize(acknowledgement);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("institution-001", document.RootElement.GetProperty("institutionId").GetString());
        Assert.Equal("identity-access", document.RootElement.GetProperty("serviceId").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("provisioningPlanVersion").GetInt32());
        Assert.Equal("provisioning-event-001", document.RootElement.GetProperty("sourceEventId").GetString());
    }

    [Fact]
    public void Consumer_side_started_contract_matches_the_producer_payload_shape()
    {
        const string json = "{\"institutionId\":\"institution-001\",\"provisioningPlanVersion\":3}";

        InstitutionProvisioningStartedV1 payload =
            Assert.IsType<InstitutionProvisioningStartedV1>(
                EventContract.Deserialize<InstitutionProvisioningStartedV1>(json));

        Assert.Equal("institution-001", payload.InstitutionId);
        Assert.Equal(3, payload.ProvisioningPlanVersion);
    }

    [Fact]
    public void Controlled_failure_contract_uses_a_stable_safe_code()
    {
        InstitutionBoundaryInitialisationFailedV1 failure = new(
            "institution-001",
            "identity-access",
            4,
            "provisioning-event-002",
            "provisioning_plan_conflict");

        string json = EventContract.Serialize(failure);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("provisioning_plan_conflict", document.RootElement.GetProperty("failureCode").GetString());
        Assert.Equal("provisioning-event-002", document.RootElement.GetProperty("sourceEventId").GetString());
    }
}
