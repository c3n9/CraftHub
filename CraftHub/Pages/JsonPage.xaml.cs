using CraftHub.AppWindows;
using CraftHub.Services;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
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

namespace CraftHub.Pages
{
    /// <summary>
    /// Логика взаимодействия для JsonPage.xaml
    /// </summary>
    public partial class JsonPage : Page
    {
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
            // Конвертируем HEX в Color
            Color color = (Color)ColorConverter.ConvertFromString("#3f51b5");

            // Создаем кисть на основе Color
            Brush brush = new SolidColorBrush(color);
            // Установите ее в выделенное состояние
            editedRow.Background = brush;
        }

        private void BBack_Click(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.dataTable.Rows.Count != 0)
            {
                var continueBack = MessageBox.Show("The completed data will be deleted, continue?", "Warning", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (continueBack == MessageBoxResult.OK)
                {
                    GlobalSettings.jsonString = string.Empty;
                    GlobalSettings.dataTable = null;
                    NavigationService.GoBack();
                }


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
            new AddNewElementWindow(newRowView, true).ShowDialog();
            GlobalSettings.jsonString = JsonConvert.SerializeObject(GlobalSettings.dataTable, Formatting.Indented);
        }

        private void BEdit_Click(object sender, RoutedEventArgs e)
        {
            if (DGJsonData.SelectedItem is DataRowView dataRow)
            {
                var dialogResult = new AddNewElementWindow(dataRow, false).ShowDialog();
                // Получаем текущую выбранную строку из DataGrid
                if (dialogResult.Value)
                {
                    DataGridRow selectedRow = (DataGridRow)DGJsonData.ItemContainerGenerator.ContainerFromItem(dataRow);
                    if (selectedRow != null)
                    {
                        if (Properties.Settings.Default.IsDarkTheme)
                        {
                            // Конвертируем HEX в Color
                            Color color = (Color)ColorConverter.ConvertFromString("#3f51b5");

                            // Создаем кисть на основе Color
                            Brush brush = new SolidColorBrush(color);
                            selectedRow.Background = brush;
                        }
                        else
                        {
                            // Конвертируем HEX в Color
                            Color color = (Color)ColorConverter.ConvertFromString("#03a9f4");

                            // Создаем кисть на основе Color
                            Brush brush = new SolidColorBrush(color);
                            selectedRow.Background = brush;
                        }
                        
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
            try
            {
                if (GlobalSettings.mainWindow.MIViewJson.IsChecked)
                {
                    Option.ViewJsonFromTable();
                }
                else
                {
                    Grid.SetColumnSpan(DGJsonData, 2);
                    TBJson.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                return;
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
            Option.ViewSurchOption();
        }

        private void DGJsonData_LoadingRow(object sender, DataGridRowEventArgs e)
        {

        }

        private void DGJsonData_Sorting(object sender, DataGridSortingEventArgs e)
        {

        }

        private void BCopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TBJson.Text))
            {
                Clipboard.SetText(TBJson.Text);
            }
        }

        private void BExport_Click(object sender, RoutedEventArgs e)
        {
            var exportJsonString = JsonConvert.SerializeObject(GlobalSettings.dataTable, Formatting.Indented);
            if (!string.IsNullOrWhiteSpace(exportJsonString))
            {
                var dialog = new SaveFileDialog() { Filter = ".json | *.json" };
                if (dialog.ShowDialog().GetValueOrDefault())
                {
                    File.WriteAllText(dialog.FileName, exportJsonString, Encoding.UTF8);
                    MessageBox.Show("Successful export", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show("Import json first");
                return;
            }
        }
    }
}
