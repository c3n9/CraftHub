using JsonConverter.AppWindows;
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
using System.Windows.Controls.Primitives;
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
    /// Логика взаимодействия для JsonPage.xaml
    /// </summary>
    public partial class JsonPage : Page
    {
        private Dictionary<DataGridRow, SolidColorBrush> originalRowColors = new Dictionary<DataGridRow, SolidColorBrush>();
        public JsonPage()
        {
            InitializeComponent();
            GlobalSettings.jsonPage = this;
            GlobalSettings.mainWindow.AddInModalWindowCheckedChanged += MainWindow_AddInModalWindowCheckedChanged;
            CBProperty.ItemsSource = GlobalSettings.dictionary.Keys.ToList();
            GlobalSettings.DisplayDataInGrid();
        }

        private void MainWindow_AddInModalWindowCheckedChanged(object sender, bool e)
        {
            if (e)
            {
                GlobalSettings.jsonPage.BAdd.Visibility = Visibility.Visible;
                GlobalSettings.jsonPage.BEdit.Visibility = Visibility.Visible;
                GlobalSettings.jsonPage.DGJsonData.CanUserAddRows = false;
                GlobalSettings.jsonPage.DGJsonData.IsReadOnly = true;
            }
            else
            {
                GlobalSettings.jsonPage.BAdd.Visibility = Visibility.Collapsed;
                GlobalSettings.jsonPage.BEdit.Visibility = Visibility.Collapsed;
                GlobalSettings.jsonPage.DGJsonData.CanUserAddRows = true;
                GlobalSettings.jsonPage.DGJsonData.IsReadOnly = false;
            }
        }

        private void DGJsonData_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            // Получите измененную строку
            DataGridRow editedRow = e.Row;
            // Установите ее в выделенное состояние
            editedRow.Background = Brushes.Yellow;
        }

        private void BBack_Click(object sender, RoutedEventArgs e)
        {
            if(GlobalSettings.dataTable.Rows.Count != 0)
            {
                GlobalSettings.jsonString = string.Empty;
                GlobalSettings.dataTable = null;
                var continueBack = MessageBox.Show("The completed data will be deleted, continue?", "Warnings", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (continueBack == MessageBoxResult.OK)
                    NavigationService.GoBack();
            }
            else
            {
                NavigationService.GoBack();
            }
                
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            GlobalSettings.mainWindow.MIImportClass.IsEnabled = false;
            GlobalSettings.mainWindow.MIImportJsonFile.IsEnabled = true;
            GlobalSettings.mainWindow.MIExportJsonFile.IsEnabled = true;

        }
        private void BAdd_Click(object sender, RoutedEventArgs e)
        {
            // Создаем новый DataRowView с пустой строкой данных
            DataRowView newRowView = GlobalSettings.dataTable.DefaultView.AddNew();
            // Новая добавленная строка находится в режиме редактирования, поэтому нужно отменить редактирование, чтобы сделать ее доступной
            newRowView.CancelEdit();
            // Показываем окно AddNewElementPage для добавления новой строки
            new AddNewElementPage(newRowView, true).ShowDialog();
            GlobalSettings.jsonString = JsonConvert.SerializeObject(GlobalSettings.dataTable, Formatting.Indented);
        }

        private void BEdit_Click(object sender, RoutedEventArgs e)
        {
            if (DGJsonData.SelectedItem is DataRowView dataRow)
            {
                var dialogResult = new AddNewElementPage(dataRow, false).ShowDialog();
                // Получаем текущую выбранную строку из DataGrid
                if (dialogResult.Value)
                {
                    DataGridRow selectedRow = (DataGridRow)DGJsonData.ItemContainerGenerator.ContainerFromItem(dataRow);
                    if (selectedRow != null)
                    {
                        selectedRow.Background = Brushes.Yellow;
                    }
                }
            }
            else
            {
                MessageBox.Show("Select the object to continue", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            GlobalSettings.jsonString = JsonConvert.SerializeObject(GlobalSettings.dataTable, Formatting.Indented);

        }

        private void DGJsonData_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GlobalSettings.mainWindow.MIViewJson.IsChecked)
            {
                GlobalSettings.ViewJsonFromTable();
            }
            else
            {
                Grid.SetColumnSpan(DGJsonData, 2);
                TBJson.Visibility = Visibility.Collapsed;
            }
        }

        private void BRemove_Click(object sender, RoutedEventArgs e)
        {
            if (DGJsonData.SelectedItems.Count == 1)
            {
                if (DGJsonData.SelectedItem is DataRowView dataRow)
                    dataRow.Row.Delete();
            }
            else if (DGJsonData.SelectedItems.Count > 1)
            {
                var dataRows = DGJsonData.SelectedItems.Cast<DataRowView>().ToList();
                if (dataRows != null)
                {
                    foreach (var dataRow in dataRows)
                    {
                        dataRow.Row.Delete();
                    }
                }
            }
            else
            {
                MessageBox.Show("Select the object to continue", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            TBJson.Text = string.Empty;
            Grid.SetColumnSpan(DGJsonData, 2);
            GlobalSettings.jsonString = JsonConvert.SerializeObject(GlobalSettings.dataTable, Formatting.Indented);
        }
        private void DGJsonData_Sorting(object sender, DataGridSortingEventArgs e)
        {
            foreach (var row in DGJsonData.Items)
            {
                var dataGridRow = DGJsonData.ItemContainerGenerator.ContainerFromItem(row) as DataGridRow;

                if (dataGridRow != null && !originalRowColors.ContainsKey(dataGridRow))
                {
                    SolidColorBrush originalColor = dataGridRow.Background as SolidColorBrush;

                    if (originalColor != null)
                    {
                        originalRowColors[dataGridRow] = originalColor;
                    }
                }
            }
            DGJsonData.Loaded += DGJsonData_Loaded;


        }

        private void DGJsonData_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void SetRowBackground(DataGrid dataGrid, DataGridRow dataGridRow, SolidColorBrush originalColor)
        {
            dataGridRow.Background = originalColor;
        }

        private void TBSurch_TextChanged(object sender, TextChangedEventArgs e)
        {
            GlobalSettings.DisplayDataInGrid();
        }

        private void CBProperty_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            GlobalSettings.DisplayDataInGrid();
        }

        private void BSurchOption_Click(object sender, RoutedEventArgs e)
        {
            GlobalSettings.ViewSurchOption();
        }
    }
}
