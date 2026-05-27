using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class TimeCoinTransaction
{
    public Guid TransactionId { get; set; }

    public Guid WalletId { get; set; }

    public string TransactionType { get; set; } = null!;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string? PaymentMethod { get; set; }

    public string? ExternalPaymentCode { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual TimeCoinWallet Wallet { get; set; } = null!;
}
