using CraftHub.Services;
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
using Microsoft.Win32;
using System.IO;

namespace CraftHub.Pages
{
    /// <summary>
    /// Логика взаимодействия для PropertiesPage.xaml
    /// </summary>
    public partial class PropertiesPage : Page
    {
        public PropertiesPage()
        {
            InitializeComponent();
            GlobalSettings.propertiesPage = this;
            Type[] basicTypes = { typeof(int), typeof(float), typeof(bool), typeof(string), typeof(double), typeof(decimal) };
            CBValues.ItemsSource = basicTypes;
            GlobalSettings.RefreshProperties();
        }
        private void BNext_Click(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.dictionary.Count == 0)
            {
                MessageBox.Show("Upload a template or add properties", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if(GlobalSettings.mainWindow.Title == "CraftHub")
            {
                var continueSave = MessageBox.Show("Do you want to save the class?", "Warning", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
                if (continueSave == MessageBoxResult.OK)
                    if (!SaveTemplate())
                        return;
            }
            NavigationService.Navigate(new JsonPage());
        }

        private void BAddProperty_Click(object sender, RoutedEventArgs e)
        {
            var error = string.Empty;
            var type = CBValues.SelectedItem as Type;
            var propertyName = TBPropertyName.Text;
            var propertyExist = GlobalSettings.dictionary.FirstOrDefault(x => x.Key == propertyName).Key;
            if (propertyExist != null)
                error += "Property with this parameter already exists\n";
            if (string.IsNullOrWhiteSpace(TBPropertyName.Text))
                error += "Enter the name of the property\n";
            if (type == null)
                error += "Select the type\n";
            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageBox.Show(error, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            GlobalSettings.dictionary.Add(propertyName, type);
            GlobalSettings.RefreshProperties();
            TBPropertyName.Text = string.Empty;
            CBValues.SelectedItem = null;
        }

        private void BRemovePropery_Click(object sender, RoutedEventArgs e)
        {
            if (DGProperties.SelectedItems.Count == 1)
            {
                var property = DGProperties.SelectedItem as dynamic;
                if (property != null)
                {
                    var rowView = property.Row.ItemArray;
                    var nameProperty = rowView[0];
                    GlobalSettings.dictionary.Remove(nameProperty.ToString());
                    GlobalSettings.RefreshProperties();
                }
            }
            else if (DGProperties.SelectedItems.Count > 1)
            {
                var properties = DGProperties.SelectedItems.Cast<dynamic>().ToList();
                if (properties.Count != 0)
                {
                    foreach (var property in properties)
                    {
                        var rowView = property.Row.ItemArray;
                        var nameProperty = rowView[0];
                        GlobalSettings.dictionary.Remove(nameProperty.ToString());
                    }
                    GlobalSettings.RefreshProperties();
                }
            }
            else
            {
                MessageBox.Show("Select property to delete", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            //GlobalSettings.mainWindow.MIImportClass.IsEnabled = true;
            //GlobalSettings.mainWindow.MIImportJsonFile.IsEnabled = false;
            //GlobalSettings.mainWindow.MIExportJsonFile.IsEnabled = false;
        }

        private void BSaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.dictionary.Count == 0)
            {
                MessageBox.Show("Upload a template or add properties", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SaveTemplate();
        }
        private bool SaveTemplate()
        {
            var dialog = new SaveFileDialog() { Filter = ".cs | *.cs" };
            string exportClass = "using System;\r\nusing System.Collections.Generic;\r\nusing System.Linq;\r\nusing System.Text;\r\nusing System.Threading.Tasks;\r\n\r\nnamespace YourNamespace\r\n{\r\n    public class ";
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                exportClass += $"{System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}{{\r\n";
                foreach (var element in GlobalSettings.dictionary)
                {
                    exportClass += $"public {element.Value.Name} {element.Key} {{ get; set; }}\r\n";
                }
                exportClass += "}\r\n}";
                File.WriteAllText(dialog.FileName, exportClass);
                GlobalSettings.mainWindow.Title = $"CraftHub — {System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}";
                return true;
            }
            else
                return false;
        }

    }
}
