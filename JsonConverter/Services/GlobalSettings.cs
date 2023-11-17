using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonConverter.Services
{
    public static class GlobalSettings
    {
        public static string jsonString;
        public static dynamic generatedObject;
        public static string exportJsonString;
        public static Dictionary<string, dynamic> dictionary = new Dictionary<string, dynamic>();
    }
}
