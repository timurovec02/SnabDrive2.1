using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnabDrive2._0
{
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
        public decimal NMCK { get; set; } = 0;
        public decimal MinPrice { get; set; } = 0;
        public decimal ResultPrice { get; set; } = 0;
        public string Winner { get; set; } = string.Empty;
        public DateTime? DateOfConclusionOfTheContract { get; set; }
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
}
