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
            GlobalSettings.jsonPage = this;
        }
        private void DisplayDataInGrid()
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
            DGJsonData.ItemsSource = null;
            DGJsonData.ItemsSource = GlobalSettings.dataTable.DefaultView;
        }

        private void DGJsonData_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            // Получите измененную строку
            DataGridRow editedRow = e.Row;
            // Установите ее в выделенное состояние
            //editedRow.Background = Brushes.Yellow;
        }

        private void DGJsonData_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            DataGridRow editedRow = e.Row;

            // Проверяем, что изменения произошли в текущей ячейке
            if (e.EditAction == DataGridEditAction.Commit)
            {
                // Устанавливаем флаг IsEdited, который будет использоваться в конвертере
                foreach (DataGridColumn column in DGJsonData.Columns)
                {
                    if (column is DataGridBoundColumn)
                    {
                        object cellValue = ((DataGridBoundColumn)column).GetCellContent(editedRow)?.DataContext;
                        if (cellValue != null)
                        {
                            ((DataRowView)cellValue)["IsEdited"] = true;
                        }
                    }
                }

                // Обновляем визуальное отображение измененной строки
                editedRow.Item = null;
                editedRow.Item = e.Row.Item;
            }

        }
    }
}
