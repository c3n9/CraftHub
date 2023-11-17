using JsonConverter.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace JsonConverter.Pages
{
    /// <summary>
    /// Логика взаимодействия для JsonPage.xaml
    /// </summary>
    public partial class JsonPage : Page
    {
        public JsonPage()
        {
            InitializeComponent();
            DisplayDataInGrid();
        }
        private void DisplayDataInGrid()
        {
            var dataTable = new DataTable();
            var jsonArray = JArray.Parse(GlobalSettings.jsonString);
            if (jsonArray.Count > 0)
            {
                var properties = GlobalSettings.dictionary;
                foreach (var property in properties)
                    dataTable.Columns.Add(property.Key, property.Value);
                foreach (var jsonItem in jsonArray)
                {
                    var dataRow = dataTable.NewRow();
                    var jsonObject = jsonItem as JObject;

                    foreach (var property in jsonObject.Properties())
                    {
                        var columnName = property.Name;
                        foreach (var propertyInDynamic in properties)
                        {
                            // Проверяем, существует ли свойство в списке properties
                            if (propertyInDynamic.Key == property.Name)
                            {
                                var columnValue = property.Value.ToObject<object>();
                                dataRow[columnName] = columnValue;
                            }
                        }
                    }
                    dataTable.Rows.Add(dataRow);
                }
            }
            GlobalSettings.exportJsonString = JsonConvert.SerializeObject(dataTable, Formatting.Indented);
            DGJsonData.ItemsSource = null;
            DGJsonData.ItemsSource = dataTable.DefaultView;
        }
    }
}
