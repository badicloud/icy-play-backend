using IcyPlay.Domain.Payments;

namespace IcyPlay.Application.Payments;

/// <summary>
/// What one kind of thing needs from online payment: how much it owes, and
/// what being paid does to it. A booking, an upgrade and an open play
/// registration each have one; the gateway, the webhook and the bookkeeping
/// are written once and shared.
/// </summary>
public interface IPaymentPurposeHandler
{
    /// <summary>The <see cref="PaymentPurpose"/> this handles.</summary>
    string Purpose
    {
        get;
    }

    /// <summary>
    /// What this customer owes for this thing right now, or why they cannot
    /// pay for it online.
    /// </summary>
    Task<PaymentResult<Payable>> DescribeAsync(Guid subjectId, Guid customerUserId, CancellationToken ct);

    /// <summary>
    /// Settles what a paid payment was for, on the tracked entities, without
    /// saving. Never throws for a payment that arrives at a bad moment: the
    /// money is real, so it is handed to a person instead.
    /// </summary>
    Task<Settlement> SettleAsync(OnlinePayment payment, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Tells people, once the settlement is saved. Best effort: a mail provider
    /// being down must not undo a payment.
    /// </summary>
    Task AfterSettledAsync(OnlinePayment payment, CancellationToken ct);
}
