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
            GlobalSettings.jsonPage = this;
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
    }
}
