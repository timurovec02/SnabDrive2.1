using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnabDrive2._0.Models
{
    public class CellColors
    {
        public int Id { get; set; }
        public int RegeditId { get; set; }
        public string ColumnName { get; set; } 
        public string ColorCode { get; set; }
        public virtual Regedit? Regedit { get; set; }
    }
}
