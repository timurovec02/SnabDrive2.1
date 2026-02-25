using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnabDrive2._0.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Login {  get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsAdmin {  get; set; }
        
    }
}
