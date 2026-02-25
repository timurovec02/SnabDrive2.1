using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnabDrive2._0
{
    public static class CurrentUser
    {
        public static int Id { get; set; }
        public static string Login { get; set; } = string.Empty;
        public static string Password { get; set; } = string.Empty;
        public static string Email { get; set; } = string.Empty;
        public static bool IsAdmin { get; set; }
        public static bool IsAuthorized { get; set; }
    }
}
