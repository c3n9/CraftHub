using JsonConverter.Pages;
using Newtonsoft.Json.Linq;
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
        public static PropertiesPage propertiesPage;
        public static MainWindow mainWindow;
        public static DataTable dataTable;
        public static void RefreshProperties()
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("Property");
            dataTable.Columns.Add("Type");
            if (GlobalSettings.dictionary != null)
            {
                foreach (var elemet in GlobalSettings.dictionary)
                {
                    var dataRow = dataTable.NewRow();
                    dataRow["Property"] = elemet.Key;
                    dataRow["Type"] = elemet.Value;
                    dataTable.Rows.Add(dataRow);
                }
            }
            propertiesPage.DGProperties.ItemsSource = dataTable.DefaultView;
        }
        public static void DisplayDataInGrid()
        {
            GlobalSettings.dataTable = new DataTable();
            var jsonArray = JArray.Parse(GlobalSettings.jsonString);
            if (jsonArray.Count > 0)
            {
                var properties = GlobalSettings.dictionary;
                foreach (var property in properties)
                    GlobalSettings.dataTable.Columns.Add(property.Key, property.Value);
                foreach (var jsonItem in jsonArray)
                {
                    var dataRow = GlobalSettings.dataTable.NewRow();
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
                    GlobalSettings.dataTable.Rows.Add(dataRow);
                }
            }
            jsonPage.DGJsonData.ItemsSource = GlobalSettings.dataTable.DefaultView;
        }
    }
}
