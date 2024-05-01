using CraftHub.Models;
using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
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
        private DataTable _dataTable;
        public DataTable DataTable
        {
            get { return _dataTable; }
            set
            {
                _dataTable = value;
                OnPropertyChanged(nameof(DataTable));
            }
        }
        public DataRowView DataRowView { get; set; }
        public List<DataRowView> DataRowViews { get; set; }

        public WorkingWithJsonViewModel()
        {
            App.WorkingWithJsonViewModel = this;
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

        public void DisplayDataInGrid()
        {
            DataTable = new DataTable();
            var properties = App.PropertiesViewModel.Properties;
           
            JArray jsonArray = new JArray();
            if (!string.IsNullOrWhiteSpace(App.jsonString))
                jsonArray = JArray.Parse(App.jsonString);
            if (jsonArray.Count > 0)
            {
                foreach (var property in properties)
                    DataTable.Columns.Add(property.Name, property.Type);
                foreach (var jsonItem in jsonArray)
                {
                    var dataRow = DataTable.NewRow();
                    var jsonObject = jsonItem as JObject;

                    foreach (var property in jsonObject.Properties())
                    {
                        var columnName = property.Name;
                        foreach (var propertyInDynamic in properties)
                        {
                            if (propertyInDynamic.Name == property.Name)
                            {
                                var columnValue = property.Value.ToObject<object>();
                                dataRow[columnName] = columnValue;
                            }
                        }
                    }
                    DataTable.Rows.Add(dataRow);
                }
            }
            else
            {
                foreach (var property in properties)
                    DataTable.Columns.Add(property.Name, property.Type);
            }
        }

        private void NavigateToPropertiesView(object parameter)
        {
            App.MainWindowViewModel.MainFrameSource = new PropertiesView();
        }

        private void OnAddCommand(object parameter)
        {
            DataRowView newRowView = DataTable.DefaultView.AddNew();
            newRowView.CancelEdit();
            App.DataRowView = newRowView;
            App.IsAdding = true;
            new AddNewElementWindow().ShowDialog();
        }

        private void OnEditCommand(object parameter)
        {
            App.IsAdding = false;
            App.DataRowView = DataRowView;
            new AddNewElementWindow().ShowDialog();
        }

        private void OnRemoveCammand(object parameter)
        {
            if (DataRowView != null)
                DataRowView.Delete();

        }

        private void OnExportCommand(object parameter)
        {
            var exportJsonString = JsonConvert.SerializeObject(DataTable, Formatting.Indented);
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
