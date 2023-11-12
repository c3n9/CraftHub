using JsonConverter.Models;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
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
    /// Логика взаимодействия для MainPage.xaml
    /// </summary>
    public partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void BImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                DataManager.jsonFile = File.ReadAllText(dialog.FileName);   
            }
            Refresh();
        }

        private void Refresh()
        {
            DGLessons.ItemsSource = DataManager.Lessons.ToList();
        }

        private void BExport_Click(object sender, RoutedEventArgs e)
        {
            var jsonData = JsonConvert.SerializeObject(DataManager.Lessons);
            var dialog = new SaveFileDialog() { Filter = ".json | *.json"};
            if(dialog.ShowDialog().GetValueOrDefault())
            {
                File.WriteAllText(dialog.FileName, jsonData, Encoding.UTF8);
            }
        }
    }
}
