using JsonConverter.Pages;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonConverter.Services
{
    public static class GlobalSettings
    {
        public static string jsonString;
        public static dynamic generatedObject;
        public static Dictionary<string, dynamic> dictionary = new Dictionary<string, dynamic>();
        public static JsonPage jsonPage;
        public static DataTable dataTable;
    }
}
