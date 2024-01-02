using CraftHub.Pages;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;

namespace CraftHub.Services
{
    public static class Option
    {
        /// <summary>
        /// Просматривает выделенные строки в таблице данных JSON и отображает их в текстовом формате JSON.
        /// </summary>
        public static void ViewJsonFromTable()
        {
            if (GlobalSettings.jsonPage.DGJsonData.SelectedItems.Count == 1)
            {
                Grid.SetColumnSpan(GlobalSettings.jsonPage.DGJsonData, 1);
                GlobalSettings.jsonPage.TBJson.Visibility = Visibility.Visible;
                var selectedData = GlobalSettings.jsonPage.DGJsonData.SelectedItem as DataRowView;
                if (selectedData != null)
                {
                    var selectedRow = selectedData.Row;
                    var rowData = selectedRow.Table.Columns.Cast<DataColumn>().ToDictionary(col => col.ColumnName, col => selectedRow[col]);
                    string json = JsonConvert.SerializeObject(rowData, Formatting.Indented);
                    GlobalSettings.jsonPage.TBJson.Text = json;
                }
            }
            else if (GlobalSettings.jsonPage.DGJsonData.SelectedItems.Count > 1)
            {
                var jsonList = new List<Dictionary<string, object>>();
                Grid.SetColumnSpan(GlobalSettings.jsonPage.DGJsonData, 1);
                foreach (var selectedData in GlobalSettings.jsonPage.DGJsonData.SelectedItems.Cast<DataRowView>())
                {
                    var selectedRow = selectedData.Row;
                    var rowData = selectedRow.Table.Columns
                        .Cast<DataColumn>()
                        .ToDictionary(col => col.ColumnName, col => selectedRow[col]);

                    jsonList.Add(rowData);
                }
                string json = JsonConvert.SerializeObject(jsonList, Formatting.Indented);
                GlobalSettings.jsonPage.TBJson.Text = json;
            }
        }
        /// <summary>
        /// Отображает или скрывает опции поиска в зависимости от текущего состояния.
        /// </summary>
        internal static void ViewSurchOption()
        {
            var contentButton = GlobalSettings.jsonPage.BSurchOption.Content as string;
            if (contentButton == "^")
            {
                GlobalSettings.jsonPage.SPSurchOption.Visibility = Visibility.Collapsed;
                GlobalSettings.jsonPage.BSurchOption.Content = "˅";
            }
            else if (contentButton == "˅")
            {
                GlobalSettings.jsonPage.SPSurchOption.Visibility = Visibility.Visible;
                GlobalSettings.jsonPage.BSurchOption.Content = "^";
            }
        }
    }
}
