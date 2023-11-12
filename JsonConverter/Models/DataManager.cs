using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonConverter.Models
{
    public class DataManager
    {
        private static List<Lesson> lessons;
        public static string jsonFile;
        public static List<Lesson> Lessons
        {
            get
            {
                lessons = GetData<List<Lesson>>();
                return lessons;
            }
            set
            {
                lessons = value;
            }
        }

        private static T GetData<T>()
        {
            var data = JsonConvert.DeserializeObject<T>(jsonFile);
            return data;
        }
    }
}
