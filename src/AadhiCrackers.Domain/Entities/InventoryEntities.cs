using AadhiCrackers.Domain.Common;
using AadhiCrackers.Domain.Enums;

namespace AadhiCrackers.Domain.Entities;

public class Warehouse : BaseEntity<Guid>
{
    public string Code { get; set; } = string.Empty; // e.g. WH-MAIN
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPrimary { get; set; } = true;

    public ICollection<StockItem> StockItems { get; set; } = new List<StockItem>();

    public Warehouse()
    {
        Id = Guid.NewGuid();
    }
}

public class StockItem : BaseEntity<Guid>
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int ReorderLevel { get; set; } = 20;

    public int QuantityAvailable => Math.Max(0, QuantityOnHand - QuantityReserved);

    public StockItem()
    {
        Id = Guid.NewGuid();
    }
}

public class StockMovement : BaseEntity<Guid>
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public StockMovementType MovementType { get; set; }
    public int QuantityChange { get; set; } // Positive for inbound, negative for outbound
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public string ReferenceType { get; set; } = string.Empty; // Order, Purchase, Adjustment, Transfer
    public string? ReferenceId { get; set; }
    public string Reason { get; set; } = string.Empty;

    public StockMovement()
    {
        Id = Guid.NewGuid();
    }
}
