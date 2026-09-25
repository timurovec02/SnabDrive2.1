namespace SnabDrive.Web.Domain;

/// <summary>Универсальная строка реестра: подходит и для основных данных, и для архива.</summary>
public class RegistryRowDto
{
    public int Id { get; set; }

    /// <summary>Заполнено только для архивных строк — Id исходной записи реестра.</summary>
    public int? IdOld { get; set; }

    public string NameLink { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;

    public int? TypeOfPurchaseId { get; set; }
    public string? TypeOfPurchaseName { get; set; }
    public string? TypeOfPurchaseColor { get; set; }

    public string PlaceOfDelivery { get; set; } = string.Empty;
    public string ReserveNumber { get; set; } = string.Empty;
    public string NationalMode { get; set; } = string.Empty;

    public DateTime? BiddingDate { get; set; }
    public DateTime? DateOfTransferForPlacement { get; set; }
    public DateTime? DateOfPlacement { get; set; }
    public DateTime? DateResults { get; set; }
    public DateTime? DateOfConclusionOfTheContract { get; set; }

    public decimal NMCK { get; set; }
    public decimal MinPrice { get; set; }
    public decimal ResultPrice { get; set; }

    public string Winner { get; set; } = string.Empty;
    public string DeliveryTime { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    public int? B2BStatusId { get; set; }
    public string? B2BStatusName { get; set; }

    public int? ExecutionStatusId { get; set; }
    public string? ExecutionStatusName { get; set; }

    public bool? IsFinished { get; set; }

    /// <summary>Только для архива.</summary>
    public DateTime? ArchivateDate { get; set; }

    public bool IsArchive => ArchivateDate.HasValue;

    /// <summary>Id, под которым хранятся цвета ячеек (в архиве — IdOld, как в WPF-версии).</summary>
    public int ColorOwnerId => IdOld ?? Id;
}
