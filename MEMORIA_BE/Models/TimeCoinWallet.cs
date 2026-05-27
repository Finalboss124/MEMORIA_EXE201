using System;
using System.Collections.Generic;

namespace MEMORIA_BE.Models;

public partial class TimeCoinWallet
{
    public Guid WalletId { get; set; }

    public Guid UserId { get; set; }

    public decimal Balance { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<TimeCoinTransaction> TimeCoinTransactions { get; set; } = new List<TimeCoinTransaction>();

    public virtual User User { get; set; } = null!;
}
