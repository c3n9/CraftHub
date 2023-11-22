using JsonConverter.Services;
using Newtonsoft.Json;
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
using System.Windows.Shapes;
using System.Xml.Linq;

namespace JsonConverter.AppWindows
{
    /// <summary>
    /// Логика взаимодействия для AddNewElementPage.xaml
    /// </summary>
    public partial class AddNewElementPage : Window
    {
        private DataRowView _selectedDataRowView;
        private bool _isAdding; // Флаг для определения добавления или редактирования

        // Конструктор страницы
        public AddNewElementPage(DataRowView selectedItem, bool isAdding)
        {
            InitializeComponent();
            _selectedDataRowView = selectedItem;
            _isAdding = isAdding;
            GenerationEditForm(); // Генерация формы для редактирования или добавления
        }

        private void GenerationEditForm()
        {
            var listElementsName = new List<string>(GlobalSettings.dictionary.Keys);
            //List<Type> listElemntsValueType = new List<Type>();
            //foreach (var value in GlobalSettings.dictionary.Values)
            //{
            //    listElemntsValueType.Add(value.GetType());
            //}
            if (!_isAdding)
            {
                // Если редактирование, отобразить существующие значения
                var dataRow = _selectedDataRowView.Row;
                var json = JsonConvert.SerializeObject(dataRow.ItemArray);
                var values = JsonConvert.DeserializeObject<List<string>>(json);

                for (int i = 0; i < values.Count; i++)
                {
                    TextBlock textBlock = new TextBlock();
                    textBlock.Text = listElementsName[i];
                    SPElements.Children.Add(textBlock);
                    TextBox textBox = new TextBox();
                    textBox.Text = values[i];
                    SPElements.Children.Add(textBox);
                    //else if (listElemntsValueType[i] == typeof(bool))
                    //{
                    //    CheckBox checkBox = new CheckBox();
                    //    checkBox.IsChecked = Convert.ToBoolean(values[i]);
                    //    SPElements.Children.Add(checkBox);
                    //}

                }
            }
            else
            {
                // Если добавление, создать пустые текстовые поля для ввода
                foreach (var elementName in listElementsName)
                {
                    TextBlock textBlock = new TextBlock();
                    textBlock.Text = elementName;

                    TextBox textBox = new TextBox();
                    SPElements.Children.Add(textBlock);
                    SPElements.Children.Add(textBox);
                }
            }
        }

        private void BSave_Click(object sender, RoutedEventArgs e)
        {
            if (_isAdding)
            {
                // Создание новой строки данных и добавление ее в DataTable
                DataRow newRow = _selectedDataRowView.Row.Table.NewRow();

                for (int i = 0; i < SPElements.Children.Count; i += 2)
                {
                    if (SPElements.Children[i] is TextBlock textBlock && SPElements.Children[i + 1] is TextBox textBox)
                    {
                        var columnName = textBlock.Text;
                        var value = textBox.Text;
                        // Установка значения в новой строке данных
                        newRow[columnName] = value;
                    }
                }

                // Добавление новой строки данных в DataTable
                _selectedDataRowView.Row.Table.Rows.Add(newRow);

                // Обработчик события LoadingRow для изменения стиля только что добавленной строки
                EventHandler<DataGridRowEventArgs> loadingRowHandler = null;
                loadingRowHandler = (gridSender, loadingRowEventArgs) =>
                {
                    DataRowView rowView = loadingRowEventArgs.Row.Item as DataRowView;

                    // Проверка, является ли текущая строка только что добавленной
                    if (rowView != null && rowView.Row == newRow)
                    {
                        loadingRowEventArgs.Row.Background = Brushes.Yellow; // Установка цвета фона в желтый

                        // Удаление обработчика после первого вызова
                        GlobalSettings.jsonPage.DGJsonData.LoadingRow -= loadingRowHandler;
                    }
                };

                // Подписка на событие LoadingRow
                GlobalSettings.jsonPage.DGJsonData.LoadingRow += loadingRowHandler;
            }
            else
            {
                // Обновление DataRowView измененными данными
                for (int i = 0; i < SPElements.Children.Count; i += 2)
                {
                    if (SPElements.Children[i] is TextBlock textBlock && SPElements.Children[i + 1] is TextBox textBox)
                    {
                        var columnName = textBlock.Text;
                        var modifiedValue = textBox.Text;
                        _selectedDataRowView[columnName] = modifiedValue;
                    }
                }
            }
            DialogResult = true;
        }
    }
}
