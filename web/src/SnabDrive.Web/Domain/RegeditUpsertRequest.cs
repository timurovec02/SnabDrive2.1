using System.ComponentModel.DataAnnotations;

namespace SnabDrive.Web.Domain;

/// <summary>Форма создания/редактирования записи реестра.</summary>
public sealed record RegeditUpsertRequest
{
    [Required(ErrorMessage = "Укажите наименование закупки")]
    [StringLength(1000, ErrorMessage = "Не более 1000 символов")]
    public string NameLink { get; set; } = string.Empty;

    [Required(ErrorMessage = "Укажите заказчика")]
    [StringLength(500, ErrorMessage = "Не более 500 символов")]
    public string Customer { get; set; } = string.Empty;

    [StringLength(500)]
    public string PlaceOfDelivery { get; set; } = string.Empty;

    [StringLength(200)]
    public string ReserveNumber { get; set; } = string.Empty;

    [StringLength(200)]
    public string NationalMode { get; set; } = string.Empty;

    public DateTime? DateOfTransferForPlacement { get; set; }
    public DateTime? DateOfPlacement { get; set; }
    public DateTime? BiddingDate { get; set; }
    public DateTime? DateResults { get; set; }
    public DateTime? DateOfConclusionOfTheContract { get; set; }

    [Range(0, 999999999, ErrorMessage = "НМЦК не может быть отрицательной")]
    public decimal NMCK { get; set; }

    [Range(0, 999999999, ErrorMessage = "Минимальная сумма не может быть отрицательной")]
    public decimal MinPrice { get; set; }

    [Range(0, 999999999, ErrorMessage = "Итоговая сумма не может быть отрицательной")]
    public decimal ResultPrice { get; set; }

    [StringLength(500)]
    public string Winner { get; set; } = string.Empty;

    [StringLength(200)]
    public string DeliveryTime { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Description { get; set; } = string.Empty;

    [StringLength(2000)]
    public string Note { get; set; } = string.Empty;

    public int? TypeOfPurchaseId { get; set; }
    public int? B2BStatusId { get; set; }
    public int? ExecutionStatusId { get; set; }

    public bool IsFinished { get; set; }
}
