using System.Text.Json;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Infrastructure.Institutions;
using Xunit;

namespace CoreBanking.CoreLedger.UnitTests;

public sealed class InstitutionProvisioningContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Provisioning_event_carries_institution_and_versioned_plan_references()
    {
        InstitutionProvisioningStartedV1 payload = new("institution-001", 4);

        string json = JsonSerializer.Serialize(payload, SerializerOptions);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal("institution-001", document.RootElement.GetProperty("institutionId").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("provisioningPlanVersion").GetInt32());
    }

    [Fact]
    public void Identity_acknowledgement_topic_is_versioned_and_service_owned()
    {
        Assert.Equal(
            "corebanking.identity-access.institution.v1",
            InstitutionBoundaryAcknowledgementConsumer.TopicForService("identity-access"));
    }

    [Fact]
    public void Successful_acknowledgement_carries_source_event_evidence()
    {
        InstitutionBoundaryInitialisedV1 payload = new(
            "institution-001",
            "identity-access",
            4,
            "source-event-001");

        string json = EventContract.Serialize(payload);
        InstitutionBoundaryInitialisedV1 restored =
            EventContract.Deserialize<InstitutionBoundaryInitialisedV1>(json)!;

        Assert.Equal(payload, restored);
    }

    [Fact]
    public void Failed_acknowledgement_carries_stable_failure_code()
    {
        InstitutionBoundaryInitialisationFailedV1 payload = new(
            "institution-001",
            "identity-access",
            4,
            "source-event-001",
            "provisioning_plan_conflict");

        string json = EventContract.Serialize(payload);
        InstitutionBoundaryInitialisationFailedV1 restored =
            EventContract.Deserialize<InstitutionBoundaryInitialisationFailedV1>(json)!;

        Assert.Equal("provisioning_plan_conflict", restored.FailureCode);
    }
}
