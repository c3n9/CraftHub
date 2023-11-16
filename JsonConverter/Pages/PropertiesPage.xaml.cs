using JsonConverter.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
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
    /// Логика взаимодействия для PropertiesPage.xaml
    /// </summary>
    public partial class PropertiesPage : Page
    {
        public PropertiesPage()
        {
            InitializeComponent();
            Type[] basicTypes = { typeof(int), typeof(bool), typeof(string) };
            CBValues.ItemsSource = basicTypes;
            RefreshProperties();
        }

        private void RefreshProperties()
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("Property");
            dataTable.Columns.Add("Type");
            if(GlobalSettings.dictionary != null)
            {
                foreach (var elemet in GlobalSettings.dictionary)
                {
                    var dataRow = dataTable.NewRow();
                    dataRow["Property"] = elemet.Key;
                    dataRow["Type"] = elemet.Value;
                    dataTable.Rows.Add(dataRow);
                }
            }
            DGProperties.ItemsSource = dataTable.DefaultView;
        }

        private void BNext_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new JsonPage());
        }

        private void BAddProperty_Click(object sender, RoutedEventArgs e)
        {
            var type = CBValues.SelectedItem as dynamic;
            GlobalSettings.dictionary.Add(TBPropertyName.Text, type);
            RefreshProperties();
        }

        private void BRemovePropery_Click(object sender, RoutedEventArgs e)
        {
            var property = DGProperties.SelectedItem as dynamic;
            var rowView = property.Row.ItemArray;
            var nameProperty = rowView[0];
            GlobalSettings.dictionary.Remove(nameProperty.ToString());
            var n = GlobalSettings.dictionary;
            RefreshProperties();
        }
    }
}
