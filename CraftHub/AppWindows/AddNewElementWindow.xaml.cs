using CraftHub.Services;
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
using System.Windows.Shapes;
using System.Xml.Linq;

namespace CraftHub.AppWindows
{
    /// <summary>
    /// Логика взаимодействия для AddNewElementPage.xaml
    /// </summary>
    public partial class AddNewElementWindow : Window
    {
        private bool isDragging = false;
        private Point startPoint;
        private DataRowView _selectedDataRowView;
        private bool _isAdding; // Флаг для определения добавления или редактирования
        public AddNewElementWindow(DataRowView selectedItem, bool isAdding)
        {
            InitializeComponent();
            _selectedDataRowView = selectedItem;
            _isAdding = isAdding;
            GenerationEditForm(); // Генерация формы для редактирования или добавления
        }

        private void GenerationEditForm()
        {
            var listElementsName = new List<string>(GlobalSettings.dictionary.Keys);
            var listValueTypes = new List<string>();
            foreach (var element in GlobalSettings.dictionary.Values)
            {
                listValueTypes.Add(element.Name);
            }
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
                    if (listValueTypes[i] == typeof(bool).Name)
                    {
                        CheckBox checkBox = new CheckBox();
                        checkBox.IsChecked = Convert.ToBoolean(values[i]);
                        SPElements.Children.Add(checkBox);
                        continue;
                    }
                    TextBox textBox = new TextBox() { Width = 200, TextWrapping = TextWrapping.Wrap, Height = 50 };
                    textBox.Text = values[i];
                    SPElements.Children.Add(textBox);

                }
            }
            else
            {
                // Если добавление, создать пустые текстовые поля для ввода
                for (int i = 0; i < listElementsName.Count; i++)
                {
                    TextBlock textBlock = new TextBlock();
                    textBlock.Text = listElementsName[i];
                    SPElements.Children.Add(textBlock);
                    if (listValueTypes[i] == typeof(bool).Name)
                    {
                        CheckBox checkBox = new CheckBox();
                        SPElements.Children.Add(checkBox);
                        continue;
                    }
                    TextBox textBox = new TextBox() { Width = 200, TextWrapping = TextWrapping.Wrap, Height = 50 };
                    SPElements.Children.Add(textBox);
                }
            }
        }

        private void BSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isAdding)
                {
                    // Создание новой строки данных и добавление ее в DataTable
                    DataRow newRow = _selectedDataRowView.Row.Table.NewRow();
                    for (int i = 0; i < SPElements.Children.Count; i += 2)
                    {
                        var textBox = SPElements.Children[i + 1] as TextBox;
                        var checkBox = SPElements.Children[i + 1] as CheckBox;
                        if (SPElements.Children[i] is TextBlock textBlock && (textBox is TextBox || checkBox is CheckBox))
                        {
                            var columnName = textBlock.Text;
                            string value = string.Empty;
                            if (textBox != null)
                            {
                                value = textBox.Text;
                            }
                            else
                            {
                                value = checkBox.IsChecked.ToString();
                            }
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
                            // Конвертируем HEX в Color
                            Color color = (Color)ColorConverter.ConvertFromString("#3f51b5");

                            // Создаем кисть на основе Color
                            Brush brush = new SolidColorBrush(color);
                            loadingRowEventArgs.Row.Background = brush; 

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
                        var textBox = SPElements.Children[i + 1] as TextBox;
                        var checkBox = SPElements.Children[i + 1] as CheckBox;
                        if (SPElements.Children[i] is TextBlock textBlock && (textBox is TextBox || checkBox is CheckBox))
                        {
                            var columnName = textBlock.Text;
                            string modifiedValue = string.Empty;
                            if (textBox != null)
                            {
                                modifiedValue = textBox.Text;
                            }
                            else
                            {
                                modifiedValue = checkBox.IsChecked.ToString();
                            }
                            _selectedDataRowView[columnName] = modifiedValue;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;
        }
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                isDragging = true;
                startPoint = e.GetPosition(this);
            }
        }

        private void TitleBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point endPoint = e.GetPosition(this);
                double offsetX = endPoint.X - startPoint.X;
                double offsetY = endPoint.Y - startPoint.Y;

                Left += offsetX;
                Top += offsetY;
            }
        }

        private void TitleBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                isDragging = false;
            }
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Минимизация окна
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Максимизация или восстановление окна
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Закрытие окна
            this.Close();
        }
    }
}
