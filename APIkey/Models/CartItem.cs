using System;

namespace APIkey.Models;

public class CartItem
{
    public int UserId { get; set; }

    public int ProductId { get; set; }

    public int Quantity { get; set; }

    public DateTime AddedAt { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;
}
