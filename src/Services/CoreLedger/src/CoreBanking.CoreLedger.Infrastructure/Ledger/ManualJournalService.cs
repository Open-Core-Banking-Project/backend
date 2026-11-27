using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreBanking.BuildingBlocks.Application.Messaging;
using CoreBanking.BuildingBlocks.Application.Tenancy;
using CoreBanking.BuildingBlocks.Domain.Identity;
using CoreBanking.BuildingBlocks.Infrastructure.Messaging;
using CoreBanking.CoreLedger.Application.Ledger;
using CoreBanking.CoreLedger.Contracts;
using CoreBanking.CoreLedger.Domain.Ledger;
using CoreBanking.CoreLedger.Infrastructure.Accounts;
using CoreBanking.CoreLedger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoreBanking.CoreLedger.Infrastructure.Ledger;

/// <summary>
/// Owns the manual-journal lifecycle: an exact-hash draft validated against the published class grants, a submitted
/// Workflow approval bound to that hash and version, and a post only when the approved quorum is satisfied. Break-glass
/// posts without the chain but records its own dual-control security evidence. Decision: P1-E05-D11.
/// </summary>
public sealed class ManualJournalService : IManualJournalService, IManualJournalApprovalApplier
{
    private const string Producer = "core-ledger";
    private const string ApprovalRequestedEventType = "approval_requested";
    private const string PostedEventType = "manual_journal_posted";
    private const string PostOperation = "ledger.manual_journal.post";
    private const int SchemaVersion = 1;

    private readonly CoreLedgerDbContext _context;
    private readonly IManualJournalPolicySource _policySource;
    private readonly IManualJournalAccountResolver _accountResolver;
    private readonly AtomicLedgerPostingService _posting;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public ManualJournalService(
        CoreLedgerDbContext context,
        IManualJournalPolicySource policySource,
        IManualJournalAccountResolver accountResolver,
        AtomicLedgerPostingService posting,
        ITenantContext tenantContext,
        TimeProvider timeProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _policySource = policySource ?? throw new ArgumentNullException(nameof(policySource));
        _accountResolver = accountResolver ?? throw new ArgumentNullException(nameof(accountResolver));
        _posting = posting ?? throw new ArgumentNullException(nameof(posting));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ManualJournalResult> CreateDraftAsync(
        CreateManualJournalDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId, command.CorrelationId);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        EffectiveManualJournalPolicy policy = await LoadPolicyAsync(command.InstitutionId, now, cancellationToken)
            .ConfigureAwait(false);
        ManualJournalContent content = await BuildContentAsync(
            command.InstitutionId,
            policy,
            command.ManualJournalId,
            command.RequestId,
            command.ReasonCode,
            command.EvidenceReference,
            command.ActorId.Value,
            command.Lines,
            now,
            cancellationToken).ConfigureAwait(false);

        ManualJournalRequestRecord? existing = await _context.ManualJournalRequests
            .SingleOrDefaultAsync(
                record => record.InstitutionId == command.InstitutionId.Value
                    && record.ManualJournalId == content.ManualJournalId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (string.Equals(existing.RequestHash, content.RequestHash, StringComparison.Ordinal))
            {
                return await ToResultAsync(existing, replayed: true, cancellationToken).ConfigureAwait(false);
            }

            throw new LedgerPostingException("manual_journal_idempotency_conflict");
        }

        bool requestIdTaken = await _context.ManualJournalRequests
            .AnyAsync(
                record => record.InstitutionId == command.InstitutionId.Value && record.RequestId == content.RequestId,
                cancellationToken)
            .ConfigureAwait(false);
        if (requestIdTaken)
        {
            throw new LedgerPostingException("manual_journal_request_id_conflict");
        }

        ManualJournalRequestRecord request = NewRequest(command.InstitutionId.Value, content, policy);
        request.CreatedAt = now;
        _context.ManualJournalRequests.Add(request);
        AddLines(request, content.Lines, policy.Policy);
        AddAudit(request, command.ActorId.Value, "staff", "ledger.manual_journal.draft", "created", command.CorrelationId, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToResultAsync(request, replayed: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManualJournalResult> SubmitAsync(
        SubmitManualJournalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId, command.CorrelationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceRequestKey);

        ManualJournalRequestRecord request = await _context.ManualJournalRequests
            .SingleOrDefaultAsync(
                record => record.InstitutionId == command.InstitutionId.Value
                    && record.ManualJournalId == command.ManualJournalId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("manual_journal_not_found");

        if (string.Equals(request.Status, "submitted", StringComparison.Ordinal)
            && string.Equals(request.WorkflowRequestId, command.RequestId.Trim(), StringComparison.Ordinal))
        {
            return await ToResultAsync(request, replayed: true, cancellationToken).ConfigureAwait(false);
        }

        ManualJournalRules.EnsureSubmittable(ParseStatus(request.Status));
        if (request.Version != command.ExpectedVersion)
        {
            throw new LedgerPostingException("manual_journal_version_stale");
        }

        if (!string.Equals(request.MakerActorId, command.ActorId.Value, StringComparison.Ordinal))
        {
            throw new LedgerPostingException("manual_journal_maker_mismatch");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        request.WorkflowRequestId = command.RequestId.Trim();
        request.Status = StatusToken(ManualJournalStatus.Submitted);
        request.SubmittedAt = now;
        request.Version++;
        AddApprovalRequested(request, command.RequestId.Trim(), command.SourceRequestKey.Trim(), command.CorrelationId, now);
        AddAudit(request, command.ActorId.Value, "staff", "ledger.manual_journal.submit", "submitted", command.CorrelationId, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToResultAsync(request, replayed: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManualJournalResult> PostWithBreakGlassAsync(
        BreakGlassManualJournalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureTenant(command.InstitutionId, command.CorrelationId);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (string.Equals(command.ActorId.Value, command.DualControlActorId?.Trim(), StringComparison.Ordinal))
        {
            throw new LedgerPostingException("manual_journal_break_glass_self_authorised");
        }

        if (string.IsNullOrWhiteSpace(command.SecurityEvidenceReference) || command.ValidUntil <= now)
        {
            throw new LedgerPostingException("manual_journal_break_glass_evidence_invalid");
        }

        EffectiveManualJournalPolicy policy = await LoadPolicyAsync(command.InstitutionId, now, cancellationToken)
            .ConfigureAwait(false);
        ManualJournalContent content = await BuildContentAsync(
            command.InstitutionId,
            policy,
            command.ManualJournalId,
            command.RequestId,
            command.ReasonCode,
            command.EvidenceReference,
            command.ActorId.Value,
            command.Lines,
            now,
            cancellationToken).ConfigureAwait(false);

        ManualJournalRequestRecord request = NewRequest(command.InstitutionId.Value, content, policy);
        request.CreatedAt = now;
        request.BreakGlass = true;
        request.BreakGlassActorId = command.ActorId.Value;
        request.DualControlActorId = command.DualControlActorId!.Trim();
        request.SecurityEvidenceReference = command.SecurityEvidenceReference.Trim();
        request.BreakGlassValidUntil = command.ValidUntil;
        _context.ManualJournalRequests.Add(request);
        AddLines(request, content.Lines, policy.Policy);

        ManualJournalRules.EnsureBreakGlassPostable(ParseStatus(request.Status));
        LedgerPostingResult posting = await PostAsync(request, content.Lines, command.CorrelationId, now, cancellationToken)
            .ConfigureAwait(false);
        ApplyPosted(
            request,
            posting,
            "ledger.manual_journal.break_glass",
            command.ActorId.Value,
            "staff",
            command.CorrelationId,
            now,
            approverCount: 0);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToResultAsync(request, replayed: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ManualJournalResult> GetAsync(
        InstitutionId institutionId,
        string manualJournalId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ManualJournalRequestRecord request = await _context.ManualJournalRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value && record.ManualJournalId == manualJournalId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("manual_journal_not_found");
        return await ToResultAsync(request, replayed: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ApplyDecisionAsync(
        InstitutionId institutionId,
        string manualJournalId,
        string requestId,
        string subjectVersion,
        string subjectHash,
        bool approved,
        string approverActorId,
        string permission,
        string entitlementVersion,
        DateTimeOffset decidedAt,
        string decisionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ManualJournalRequestRecord request = await _context.ManualJournalRequests
            .SingleOrDefaultAsync(
                record => record.InstitutionId == institutionId.Value && record.ManualJournalId == manualJournalId.Trim(),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The manual journal does not exist for this institution.");

        if (string.Equals(request.Status, "posted", StringComparison.Ordinal))
        {
            return "already_posted";
        }

        if (!string.Equals(request.WorkflowRequestId, requestId, StringComparison.Ordinal)
            || !string.Equals(request.RequestHash, subjectHash, StringComparison.Ordinal)
            || !string.Equals(request.Version.ToString(CultureInfo.InvariantCulture), subjectVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The manual journal decision does not match the exact submitted version and content hash.");
        }

        ManualJournalRules.EnsureDecidable(ParseStatus(request.Status));
        if (approved)
        {
            ManualJournalRules.EnsureApproverIsNotMaker(approverActorId, request.MakerActorId);
        }

        string effectivePermission = string.IsNullOrWhiteSpace(permission)
            ? ManualJournalApprovalV1.RequiredPermission
            : permission.Trim();
        bool isFinanceHead = string.Equals(effectivePermission, request.FinanceHeadPermission, StringComparison.Ordinal);

        ManualJournalApprovalRecord[] recorded = await _context.ManualJournalApprovals
            .Where(record => record.InstitutionId == institutionId.Value && record.ManualJournalId == request.ManualJournalId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!recorded.Any(record => string.Equals(record.DecisionId, decisionId, StringComparison.Ordinal)))
        {
            _context.ManualJournalApprovals.Add(new ManualJournalApprovalRecord
            {
                InstitutionId = institutionId.Value,
                ManualJournalId = request.ManualJournalId,
                DecisionId = decisionId.Trim(),
                ApproverActorId = approverActorId.Trim(),
                Permission = effectivePermission,
                IsFinanceHead = isFinanceHead,
                EntitlementVersion = entitlementVersion,
                DecidedAt = decidedAt,
            });
        }

        if (!approved)
        {
            request.Status = StatusToken(ManualJournalStatus.Rejected);
            request.RefusalCode = "manual_journal_rejected";
            request.DecidedAt = decidedAt;
            request.Version++;
            AddAudit(request, approverActorId, "staff", "ledger.manual_journal.approve", "rejected", correlationId, decidedAt);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return "rejected";
        }

        List<ManualJournalApproval> approvals = recorded
            .Select(record => new ManualJournalApproval(
                record.DecisionId,
                record.ApproverActorId,
                record.Permission,
                record.IsFinanceHead,
                record.DecidedAt))
            .ToList();
        approvals.Add(new ManualJournalApproval(
            decisionId.Trim(),
            approverActorId.Trim(),
            effectivePermission,
            isFinanceHead,
            decidedAt));

        ManualJournalApprovalRequirement requirement = new(
            request.RequiredApprovers,
            request.RequiresFinanceHead);
        if (!ManualJournalPolicy.IsSatisfiedBy(requirement, approvals, request.MakerActorId))
        {
            AddAudit(request, approverActorId, "staff", "ledger.manual_journal.approve", "awaiting_quorum", correlationId, decidedAt);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return "awaiting_quorum";
        }

        request.Status = StatusToken(ManualJournalStatus.Approved);
        request.ApprovedByActorId = approverActorId.Trim();
        request.DecidedAt = decidedAt;
        request.Version++;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ManualJournalLine> lines = await ReadLinesAsync(request, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        LedgerPostingResult posting = await PostAsync(request, lines, correlationId, now, cancellationToken)
            .ConfigureAwait(false);
        ApplyPosted(
            request,
            posting,
            "ledger.manual_journal.post",
            approverActorId,
            "staff",
            correlationId,
            now,
            approvals.DistinctBy(approval => approval.ApproverActorId, StringComparer.Ordinal).Count());
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return "posted";
    }

    private static ManualJournalRequestRecord NewRequest(
        string institutionId,
        ManualJournalContent content,
        EffectiveManualJournalPolicy policy)
    {
        ManualJournalApprovalRequirement requirement = policy.Policy.RequirementFor(content.TotalAmount, content.AccountClasses);
        return new ManualJournalRequestRecord
        {
            InstitutionId = institutionId,
            ManualJournalId = content.ManualJournalId,
            RequestId = content.RequestId,
            Status = StatusToken(ManualJournalStatus.Draft),
            LegalEntityCode = content.LegalEntityCode,
            CurrencyCode = content.Currency.Value,
            TotalAmount = content.TotalAmount,
            ReasonCode = content.ReasonCode,
            EvidenceReference = content.EvidenceReference,
            RequestHash = content.RequestHash,
            PolicyVersionId = policy.VersionId,
            PolicyContentHash = policy.ContentHash,
            RequiredApprovers = requirement.RequiredApprovers,
            RequiresFinanceHead = requirement.RequiresFinanceHead,
            FinanceHeadPermission = policy.Policy.FinanceHeadPermission,
            MakerActorId = content.MakerActorId,
            Version = 1,
        };
    }

    private async Task<ManualJournalContent> BuildContentAsync(
        InstitutionId institutionId,
        EffectiveManualJournalPolicy policy,
        string manualJournalId,
        string requestId,
        string reasonCode,
        string evidenceReference,
        string makerActorId,
        IReadOnlyList<ManualJournalLineCommand> commandLines,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ManualJournalAccountLookup lookup = await _accountResolver.ResolveAsync(
            institutionId,
            commandLines.Select(line => line.LedgerAccountId).ToArray(),
            now,
            cancellationToken).ConfigureAwait(false);
        if (!lookup.Found || lookup.Accounts is null)
        {
            throw new LedgerPostingException(lookup.RefusalCode ?? "ledger_account_not_found");
        }

        IReadOnlyList<ManualJournalAccountClassification> accounts = lookup.Accounts;
        if (accounts.Select(account => account.Currency).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new LedgerPostingException("manual_journal_currency_mismatch");
        }

        Dictionary<string, ManualJournalAccountClassification> byId = accounts.ToDictionary(
            account => account.LedgerAccountId,
            StringComparer.Ordinal);
        List<ManualJournalLine> lines = new(commandLines.Count);
        for (int index = 0; index < commandLines.Count; index++)
        {
            ManualJournalLineCommand input = commandLines[index];
            if (!byId.TryGetValue(input.LedgerAccountId?.Trim() ?? string.Empty, out ManualJournalAccountClassification? account))
            {
                throw new LedgerPostingException("ledger_account_not_found");
            }

            ManualJournalClassGrant grant = policy.Policy.ClassForRole(account.Role)
                ?? throw new LedgerPostingException("manual_journal_class_not_approved");
            lines.Add(new ManualJournalLine(
                index + 1,
                account.LedgerAccountId,
                account.ChartAccountCode,
                account.Role,
                grant.Code,
                ParseDirection(input.Direction),
                input.Amount,
                ParseComponent(input.Component)));
        }

        return ManualJournalContent.Create(
            policy.Policy,
            manualJournalId,
            requestId,
            institutionId.Value,
            accounts[0].LegalEntityCode,
            accounts[0].Currency,
            reasonCode,
            evidenceReference,
            makerActorId,
            lines);
    }

    private async Task<EffectiveManualJournalPolicy> LoadPolicyAsync(
        InstitutionId institutionId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        ManualJournalPolicyLookup lookup = await _policySource
            .GetAsync(institutionId, asOf, cancellationToken)
            .ConfigureAwait(false);
        if (!lookup.Found
            || lookup.Policy is null
            || string.IsNullOrWhiteSpace(lookup.VersionId)
            || string.IsNullOrWhiteSpace(lookup.ContentHash))
        {
            throw new LedgerPostingException(lookup.RefusalCode ?? "manual_journal_policy_missing");
        }

        return new EffectiveManualJournalPolicy(lookup.Policy, lookup.VersionId!, lookup.ContentHash!);
    }

    private async Task<IReadOnlyList<ManualJournalLine>> ReadLinesAsync(
        ManualJournalRequestRecord request,
        CancellationToken cancellationToken)
    {
        ManualJournalLineRecord[] lines = await _context.ManualJournalLines
            .AsNoTracking()
            .Where(record => record.InstitutionId == request.InstitutionId
                && record.ManualJournalId == request.ManualJournalId)
            .OrderBy(record => record.Sequence)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return lines.Select(line => new ManualJournalLine(
                line.Sequence,
                line.LedgerAccountId,
                line.ChartAccountCode,
                ParseRole(line.AccountRole),
                line.AccountClass,
                string.Equals(line.Direction, "debit", StringComparison.Ordinal)
                    ? DebitCreditIndicator.Debit
                    : DebitCreditIndicator.Credit,
                line.Amount,
                ParseComponent(line.Component)))
            .ToArray();
    }

    private async Task<LedgerPostingResult> PostAsync(
        ManualJournalRequestRecord request,
        IReadOnlyList<ManualJournalLine> lines,
        string correlationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        BusinessDateControlRecord businessDate = await _context.BusinessDateControls
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new LedgerPostingException("manual_journal_business_date_unavailable");
        FinancialDates dates = FinancialDates.Create(
            now,
            businessDate.CurrentBusinessDate,
            businessDate.CurrentBusinessDate,
            businessDate.CurrentBusinessDate);
        AccountingScope scope = new(
            request.InstitutionId,
            request.LegalEntityCode,
            CurrencyCode.Parse(request.CurrencyCode));
        JournalEntry journal = JournalEntry.Create(
            request.ManualJournalId + "-J",
            request.ManualJournalId,
            request.ManualJournalId + "-JR",
            request.PolicyVersionId,
            scope,
            dates,
            lines.Select(line => JournalLine.Create(
                request.ManualJournalId + "-L" + line.Sequence.ToString(CultureInfo.InvariantCulture),
                line.Sequence,
                line.LedgerAccountId,
                line.Direction,
                line.Component,
                Money.Create(line.Amount, request.CurrencyCode),
                scope,
                request.ReasonCode)));
        FinancialTransaction transaction = FinancialTransaction.CreateReadyToPost(
            request.ManualJournalId,
            request.ManualJournalId,
            request.InstitutionId,
            Producer,
            request.ManualJournalId + "-SRC",
            "manual_journal.v1",
            request.RequestHash,
            dates);

        Dictionary<string, long> controlVersions = new(StringComparer.Ordinal);
        foreach (string accountId in lines.Select(line => line.LedgerAccountId).Distinct(StringComparer.Ordinal))
        {
            controlVersions[accountId] = await AccountControlVersionReader
                .ReadAsync(_context, accountId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await _posting.PostAsync(
            new AtomicLedgerPostingCommand(
                PostingIdempotencyScope.Create(request.InstitutionId, PostOperation, Producer, request.ManualJournalId),
                transaction,
                journal,
                new LedgerPostingControlContext("operations", "manual_journal", controlVersions),
                request.BreakGlass ? request.BreakGlassActorId! : request.MakerActorId,
                "staff",
                correlationId),
            cancellationToken).ConfigureAwait(false);
    }

    private void ApplyPosted(
        ManualJournalRequestRecord request,
        LedgerPostingResult posting,
        string permission,
        string actorId,
        string actorType,
        string correlationId,
        DateTimeOffset now,
        int approverCount)
    {
        request.Status = StatusToken(ManualJournalStatus.Posted);
        request.FinancialTransactionId = posting.FinancialTransactionId;
        request.PostedAt = now;
        request.Version++;
        AddAudit(request, actorId, actorType, permission, "posted", correlationId, now);
        AddPostedOutbox(request, posting, correlationId, now, approverCount);
    }

    private void AddLines(
        ManualJournalRequestRecord request,
        IReadOnlyList<ManualJournalLine> lines,
        ManualJournalPolicy policy)
    {
        foreach (ManualJournalLine line in lines)
        {
            ManualJournalClassGrant grant = policy.Classes.Single(item => item.Code == line.AccountClass);
            _context.ManualJournalLines.Add(new ManualJournalLineRecord
            {
                InstitutionId = request.InstitutionId,
                ManualJournalId = request.ManualJournalId,
                Sequence = line.Sequence,
                LedgerAccountId = line.LedgerAccountId,
                ChartAccountCode = line.ChartAccountCode,
                AccountRole = line.Role.ToString(),
                AccountClass = line.AccountClass,
                Direction = line.Direction == DebitCreditIndicator.Debit ? "debit" : "credit",
                Amount = line.Amount,
                Component = ComponentToken(line.Component),
                RequiresCompensatingAccount = grant.RequiresCompensatingAccount,
            });
        }
    }

    private void AddAudit(
        ManualJournalRequestRecord request,
        string actorId,
        string actorType,
        string permission,
        string state,
        string correlationId,
        DateTimeOffset now) =>
        _context.InstitutionAuditEvents.Add(new InstitutionAuditRecord
        {
            InstitutionId = request.InstitutionId,
            AuditEventId = Guid.NewGuid().ToString("N"),
            ActorId = actorId,
            ActorType = actorType,
            Permission = permission,
            Action = permission,
            ResourceType = "ledger.manual_journal",
            ResourceId = request.ManualJournalId,
            Outcome = string.Equals(state, "rejected", StringComparison.Ordinal) ? "rejected" : "succeeded",
            Reason = state,
            CorrelationId = correlationId,
            AfterReference = request.RequestHash,
            OccurredAt = now,
        });

    private void AddApprovalRequested(
        ManualJournalRequestRecord request,
        string requestId,
        string sourceRequestKey,
        string correlationId,
        DateTimeOffset now)
    {
        string eventId = Guid.NewGuid().ToString("N");
        ApprovalRequestedV1 payload = new(
            request.InstitutionId,
            requestId,
            sourceRequestKey,
            ManualJournalApprovalV1.WorkflowKey,
            ManualJournalApprovalV1.WorkflowVersionId,
            ManualJournalApprovalV1.WorkflowDefinitionHash,
            ManualJournalApprovalV1.SourceService,
            ManualJournalApprovalV1.SubjectType,
            request.ManualJournalId,
            request.Version.ToString(CultureInfo.InvariantCulture),
            request.RequestHash,
            request.MakerActorId,
            ManualJournalApprovalV1.RequiredPermission,
            correlationId,
            now);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = ApprovalRequestedEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = request.InstitutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = requestId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = request.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = ApprovalRequestedEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, "approval", 1),
            BusinessReference = requestId,
            CorrelationId = correlationId,
            TraceParent = System.Diagnostics.Activity.Current?.Id,
            TraceState = System.Diagnostics.Activity.Current?.TraceStateString,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }

    private void AddPostedOutbox(
        ManualJournalRequestRecord request,
        LedgerPostingResult posting,
        string correlationId,
        DateTimeOffset now,
        int approverCount)
    {
        string eventId = Guid.NewGuid().ToString("N");
        ManualJournalPostedV1 payload = new(
            request.ManualJournalId,
            request.RequestId,
            posting.FinancialTransactionId,
            posting.TransactionReference,
            posting.JournalEntryId,
            request.LegalEntityCode,
            request.CurrencyCode,
            request.TotalAmount,
            request.ReasonCode,
            request.EvidenceReference,
            request.PolicyVersionId,
            approverCount,
            request.BreakGlass,
            posting.PostedAt);
        IntegrationEventEnvelope envelope = new()
        {
            EventId = eventId,
            EventType = PostedEventType,
            SchemaVersion = SchemaVersion,
            OccurredAt = now,
            InstitutionId = request.InstitutionId,
            CorrelationId = correlationId,
            Producer = Producer,
            BusinessReference = request.ManualJournalId,
            Payload = EventContract.Serialize(payload),
        };
        _context.OutboxMessages.Add(new OutboxRecord
        {
            InstitutionId = request.InstitutionId,
            MessageId = Guid.NewGuid().ToString("N"),
            EventId = eventId,
            EventType = PostedEventType,
            SchemaVersion = SchemaVersion,
            Topic = EventContract.TopicFor(Producer, "ledger-manual-journals", 1),
            BusinessReference = request.ManualJournalId,
            CorrelationId = correlationId,
            Producer = Producer,
            Envelope = EventContract.Serialize(envelope),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = now,
            CreatedAt = now,
        });
    }

    private async Task<ManualJournalResult> ToResultAsync(
        ManualJournalRequestRecord request,
        bool replayed,
        CancellationToken cancellationToken)
    {
        string[] classes = await _context.ManualJournalLines
            .AsNoTracking()
            .Where(record => record.InstitutionId == request.InstitutionId
                && record.ManualJournalId == request.ManualJournalId)
            .Select(record => record.AccountClass)
            .Distinct()
            .OrderBy(code => code)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        int approvals = await _context.ManualJournalApprovals
            .AsNoTracking()
            .CountAsync(
                record => record.InstitutionId == request.InstitutionId
                    && record.ManualJournalId == request.ManualJournalId,
                cancellationToken)
            .ConfigureAwait(false);
        return new ManualJournalResult(
            request.InstitutionId,
            request.ManualJournalId,
            request.RequestId,
            request.Status,
            request.LegalEntityCode,
            request.CurrencyCode,
            request.TotalAmount,
            request.ReasonCode,
            request.EvidenceReference,
            request.RequestHash,
            request.PolicyVersionId,
            request.PolicyContentHash,
            request.RequiredApprovers,
            request.RequiresFinanceHead,
            classes,
            request.MakerActorId,
            approvals,
            request.ApprovedByActorId,
            request.WorkflowRequestId,
            request.FinancialTransactionId,
            request.RefusalCode,
            request.CreatedAt,
            request.SubmittedAt,
            request.DecidedAt,
            request.PostedAt,
            request.Version,
            replayed);
    }

    private void EnsureTenant(InstitutionId institutionId, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(institutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (!string.Equals(
                _tenantContext.CurrentInstitution?.Value,
                institutionId.Value,
                StringComparison.Ordinal))
        {
            throw new LedgerPostingException("institution_context_mismatch");
        }
    }

    private static ManualJournalStatus ParseStatus(string value) => value switch
    {
        "draft" => ManualJournalStatus.Draft,
        "submitted" => ManualJournalStatus.Submitted,
        "approved" => ManualJournalStatus.Approved,
        "rejected" => ManualJournalStatus.Rejected,
        "posted" => ManualJournalStatus.Posted,
        _ => throw new LedgerPostingException("manual_journal_state_invalid"),
    };

    private static string StatusToken(ManualJournalStatus status) => status switch
    {
        ManualJournalStatus.Draft => "draft",
        ManualJournalStatus.Submitted => "submitted",
        ManualJournalStatus.Approved => "approved",
        ManualJournalStatus.Rejected => "rejected",
        ManualJournalStatus.Posted => "posted",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static DebitCreditIndicator ParseDirection(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "debit" => DebitCreditIndicator.Debit,
        "credit" => DebitCreditIndicator.Credit,
        _ => throw new LedgerPostingException("journal_line_direction_invalid"),
    };

    private static LedgerAccountRole ParseRole(string value) =>
        Enum.TryParse(value, ignoreCase: false, out LedgerAccountRole role) && Enum.IsDefined(role)
            ? role
            : throw new LedgerPostingException("manual_journal_class_role_mismatch");

    private static AccountingComponentType ParseComponent(string value) => (value ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "principal" => AccountingComponentType.Principal,
        "fee" => AccountingComponentType.Fee,
        "tax" => AccountingComponentType.Tax,
        "commission" => AccountingComponentType.Commission,
        "penalty" => AccountingComponentType.Penalty,
        "interest" => AccountingComponentType.Interest,
        "waiver" => AccountingComponentType.Waiver,
        "rounding" => AccountingComponentType.Rounding,
        _ => throw new LedgerPostingException("accounting_component_invalid"),
    };

    private static string ComponentToken(AccountingComponentType component) =>
        component.ToString().ToLowerInvariant();

    private sealed record EffectiveManualJournalPolicy(ManualJournalPolicy Policy, string VersionId, string ContentHash);
}
