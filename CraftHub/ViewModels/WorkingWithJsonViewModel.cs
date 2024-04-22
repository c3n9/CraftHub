using CraftHub.Models;
using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.ObjectModel;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Windows.Input;

namespace CraftHub.ViewModels
{
    internal class WorkingWithJsonViewModel : BaseViewModel
    {
        public ICommand NavigateToPropertiesViewCommand { get; set; }
        public ICommand AddCommand { get; set; }
        public ICommand EditCommand { get; set; }
        public ICommand RemoveCommand { get; set; }
        public ICommand ExportCommand { get; set; }
        public DataTable DataTable { get; set; }

        public WorkingWithJsonViewModel()
        {
            try
            {
                DisplayDataInGrid();
            }
            catch (Exception ex)
            {

            }

            NavigateToPropertiesViewCommand = new DelegateCommand(NavigateToPropertiesView);
            AddCommand = new DelegateCommand(OnAddCommand);
            EditCommand = new DelegateCommand(OnEditCommand);
            RemoveCommand = new DelegateCommand(OnRemoveCammand);
            ExportCommand = new DelegateCommand(OnExportCommand);
        }

        private void DisplayDataInGrid()
        {
            DataTable = new DataTable();
            var properties = App.PropertiesViewModel.Properties;
            foreach (var property in properties)
            {
                DataTable.Columns.Add(property.Name, property.Type);
            }
        }

        private void NavigateToPropertiesView(object parameter)
        {
            App.MainWindowViewModel.MainFrameSource = new PropertiesView();
        }

        private void OnAddCommand(object parameter)
        {
        }

        private void OnEditCommand(object parameter)
        {

        }

        private void OnRemoveCammand(object parameter)
        {

        }

        private void OnExportCommand(object parameter)
        {

        }
    }
}
