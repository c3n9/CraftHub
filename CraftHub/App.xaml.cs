using CraftHub.ViewModels;
using CraftHub.Views;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace CraftHub
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {
        internal static MainWindowViewModel MainWindowViewModel;
        internal static PropertiesViewModel PropertiesViewModel;
        internal static AddNewElementWindow AddNewElementWindow;
        internal static WorkingWithJsonViewModel WorkingWithJsonViewModel;
        public static bool IsAdding = false;
        public static DataRowView DataRowView { get; set; }
    }
}
