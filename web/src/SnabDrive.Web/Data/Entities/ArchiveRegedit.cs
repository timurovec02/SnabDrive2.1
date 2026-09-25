namespace SnabDrive.Web.Data.Entities;

/// <summary>
/// Архивная запись реестра. Соответствует существующей таблице [ArchiveRegedit].
/// <see cref="IdOld"/> хранит Id исходной записи из таблицы [Regedit].
/// </summary>
public class ArchiveRegedit
{
    public int Id { get; set; }

    public int IdOld { get; set; }

    public string NameLink { get; set; } = string.Empty;
    public string PlaceOfDelivery { get; set; } = string.Empty;
    public string ReserveNumber { get; set; } = string.Empty;
    public string NationalMode { get; set; } = string.Empty;

    public DateTime? DateOfTransferForPlacement { get; set; }
    public DateTime? DateOfPlacement { get; set; }
    public DateTime? BiddingDate { get; set; }
    public DateTime? DateResults { get; set; }
    public DateTime? DateOfConclusionOfTheContract { get; set; }

    public decimal NMCK { get; set; }
    public decimal MinPrice { get; set; }
    public decimal ResultPrice { get; set; }

    public string Winner { get; set; } = string.Empty;
    public string DeliveryTime { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;

    public int? TypeOfPurchaseId { get; set; }
    public int? B2BStatusId { get; set; }
    public int? ExecutionStatusId { get; set; }

    public DateTime? ArchivateDate { get; set; }

    public virtual B2BStatus? B2BStatus { get; set; }
    public virtual TypeOfPurchase? TypeOfPurchase { get; set; }
    public virtual ExecutionStatus? ExecutionStatus { get; set; }
}
