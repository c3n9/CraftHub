using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonConverter.Models
{
    public partial class Lesson
    {
        public bool IsEnabled { get; set; }
        public int Level { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsSimulatorMode { get; set; }
        public int LessonBlockId { get; set; }
        public int LessonNumber { get; set; }
        public int PagesCount { get; set; }
        public bool IsCommon { get; set; }
        public string RobotPrefab { get; set; }
        public string FieldPrefab { get; set; }

    }
}
