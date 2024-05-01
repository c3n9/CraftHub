﻿using System;
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
    /// Логика взаимодействия для WorkingWithJsonView.xaml
    /// </summary>
    public partial class WorkingWithJsonView : Page
    {
        public WorkingWithJsonView()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            App.MainWindow.MIExportJsonFile.IsEnabled = true;
            App.MainWindow.MIImportJsonFile.IsEnabled = true;
            App.MainWindow.MIImportClass.IsEnabled = false;
        }
    }
}
