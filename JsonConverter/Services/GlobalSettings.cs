using JsonConverter.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using JsonConverter.AppWindows;

namespace JsonConverter.Services
{
    public static class GlobalSettings
    {
        public static string jsonString = String.Empty;
        public static dynamic generatedObject;
        public static string nameTemplate;
        public static Dictionary<string, dynamic> dictionary = new Dictionary<string, dynamic>();
        public static JsonPage jsonPage;
        public static PropertiesPage propertiesPage;
        public static MainWindow mainWindow;
        public static DataTable dataTable;
        /// <summary>
        /// Обновляет свойства в таблице на странице свойств.
        /// </summary>
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
        /// <summary>
        /// Отображает данные в таблице на основе фильтрации по свойству и значению.
        /// </summary>
        public static void DisplayDataInGrid()
        {
            GlobalSettings.dataTable = new DataTable();
            JArray jsonArray = new JArray();
            var surchData = GlobalSettings.jsonPage.TBSurch.Text;
            var propertyForSurch = GlobalSettings.jsonPage.CBProperty.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(GlobalSettings.jsonString))
                jsonArray = JArray.Parse(GlobalSettings.jsonString);
            if (!string.IsNullOrWhiteSpace(surchData) && propertyForSurch != null)
            {
                jsonArray = new JArray(jsonArray
                    .Where(jsonItem =>
                    {
                        var jsonObject = jsonItem as JObject;
                        // Проверяем, что объект и его свойство для сортировки существуют
                        var propertyValue = jsonObject?[propertyForSurch]?.ToString();
                        return jsonObject != null && propertyValue != null && propertyValue.IndexOf(surchData, StringComparison.OrdinalIgnoreCase) >= 0;
                    })
                );
            }
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
            else
            {
                var properties = GlobalSettings.dictionary;
                foreach (var property in properties)
                    GlobalSettings.dataTable.Columns.Add(property.Key, property.Value);
            }
            jsonPage.DGJsonData.ItemsSource = GlobalSettings.dataTable.DefaultView;
        }
        
    }
}
