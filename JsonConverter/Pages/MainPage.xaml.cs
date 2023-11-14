using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace JsonConverter.Pages
{
    /// <summary>
    /// Логика взаимодействия для MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        string jsonString;
        public MainPage()
        {
            InitializeComponent();
        }

        private void BImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".json | *.json" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                jsonString = File.ReadAllText(dialog.FileName);
                var dataTable = DisplayDataInGrid(jsonString);
                DGJsonData.ItemsSource = dataTable.DefaultView;
                TBJson.Text = jsonString.ToString();
            }
        }

        private DataTable DisplayDataInGrid(string json)
        {
            var dataTable = new DataTable();
            var jsonArray = JArray.Parse(json);
            if (jsonArray.Count > 0)
            {
                foreach (var property in jsonArray[0] as JObject)
                {
                    dataTable.Columns.Add(property.Key, typeof(object));
                }
            }
            foreach (var jsonItem in jsonArray)
            {
                var dataRow = dataTable.NewRow();
                var jsonObject = jsonItem as JObject;

                foreach (var property in jsonObject.Properties())
                {
                    var columnName = property.Name;
                    var columnValue = property.Value.ToObject<object>();
                    dataRow[columnName] = columnValue;
                }

                dataTable.Rows.Add(dataRow);
            }
            return dataTable;
        }

        private void BExport_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BSelectModel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".cs | *.cs" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                

            }
        }
    }
}
