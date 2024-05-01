using System;
using System.Collections.Generic;
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

namespace CraftHub.Views
{
    /// <summary>
    /// Логика взаимодействия для PropertiesView.xaml
    /// </summary>
    public partial class PropertiesView : Page
    {
        public PropertiesView()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App.MainWindow.MIExportJsonFile.IsEnabled = false;
            App.MainWindow.MIImportJsonFile.IsEnabled = false;
            App.MainWindow.MIImportClass.IsEnabled = true;
        }
    }
}
