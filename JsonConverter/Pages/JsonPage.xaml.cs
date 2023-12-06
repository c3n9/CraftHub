﻿using JsonConverter.AppWindows;
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
            NavigationService.GoBack();
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
                MessageBox.Show("Select object");
                return;
            }
        }

        private void DGJsonData_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GlobalSettings.mainWindow.MIViewJson.IsChecked)
            {
                Grid.SetColumnSpan(DGJsonData, 1);
                TBJson.Visibility = Visibility.Visible;
                var g = DGJsonData.SelectedItem as DataRowView;
                string json = JsonConvert.SerializeObject(g.Row.ItemArray, Formatting.Indented);
                TBJson.Text = json;
            }
            else
            {
                Grid.SetColumnSpan(DGJsonData, 2);
                TBJson.Visibility = Visibility.Collapsed;
            }
        }

        private void BRemove_Click(object sender, RoutedEventArgs e)
        {
            if (DGJsonData.SelectedItem is DataRowView dataRow)
            {
                dataRow.Row.Delete();
            }
            else
            {
                MessageBox.Show("Select object");
                return;
            }
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
    }
}
