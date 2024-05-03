using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using CraftHub.Views;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CraftHub.Models;
using System.Collections.ObjectModel;
using System.IO;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Data;

namespace CraftHub.ViewModels
{
    internal class WorkingAreaViewModel : BaseViewModel
    {
        public ICommand AddPropertyCommand { get; private set; }
        public ICommand AddCommand { get; set; }
        public ICommand EditCommand { get; set; }
        public ICommand RemoveCommand { get; set; }
        public ICommand ExportCommand { get; set; }

        private ObservableCollection<UIElement> _uIElemetsCollection;
        public ObservableCollection<UIElement> UIElemetsCollection
        {
            get { return _uIElemetsCollection; }
            set
            {
                _uIElemetsCollection = value;
                OnPropertyChanged(nameof(UIElemetsCollection));
            }
        }

        private Type _selectedType;
        public Type SelectedType
        {
            get { return _selectedType; }
            set
            {
                if (_selectedType != value)
                {
                    _selectedType = value;
                    OnPropertyChanged(nameof(SelectedType));
                }
            }
        }
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
        private Visibility _buttonVisibility;
        public Visibility ButtonVisibility
        {
            get { return _buttonVisibility; }
            set
            {
                _buttonVisibility = value;
                OnPropertyChanged(nameof(ButtonVisibility));
            }
        }
        private bool _isEditTable;
        public bool IsEditTable
        {
            get { return _isEditTable; }
            set
            {
                _isEditTable = value;
                OnPropertyChanged(nameof(IsEditTable));
            }
        }
        private bool _canUserAddRows;
        public bool CanUserAddRows
        {
            get { return _canUserAddRows; }
            set
            {
                _canUserAddRows = value;
                OnPropertyChanged(nameof(CanUserAddRows));
            }
        }


        public ObservableCollection<PropertyModel> Properties { get; set; }
        public ObservableCollection<Type> AvailableTypes { get; set; }
        public DataRowView DataRowView { get; set; }

        DataGrid dataGrid { get; set; }
        public WorkingAreaViewModel()
        {
            DataTable = new DataTable();
            UIElemetsCollection = new ObservableCollection<UIElement>();

            App.WorkingAreaViewModel = this;

            Properties = new ObservableCollection<PropertyModel>();

            AvailableTypes = new ObservableCollection<Type>
            {
                typeof(int),
                typeof(float),
                typeof(bool),
                typeof(string),
                typeof(double),
                typeof(decimal)
            };


            AddPropertyCommand = new DelegateCommand(OnAddPropertyCommand);
            AddCommand = new DelegateCommand(OnAddCommand);
            EditCommand = new DelegateCommand(OnEditCommand);
            RemoveCommand = new DelegateCommand(OnRemoveCammand);
            ExportCommand = new DelegateCommand(OnExportCommand);
            dataGrid = new DataGrid()
            {
                ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star),
                AutoGenerateColumns = true,
                FontSize = 18,
                CanUserAddRows = false,
                IsReadOnly = true,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            dataGrid.SetBinding(DataGrid.SelectedItemProperty, new Binding("DataRowView"));
            //dataGrid.SetBinding(DataGrid.CanUserAddRowsProperty, new Binding("CanUserAddRows"));
            //dataGrid.SetBinding(DataGrid.IsReadOnlyProperty, new Binding("IsEditTable"));
            dataGrid.DataContext = this;

            UIElemetsCollection.Add(dataGrid);

            //App.MainWindow.AddInModalWindowCheckedChanged += MainWindow_AddInModalWindowCheckedChanged;

        }

        private void MainWindow_AddInModalWindowCheckedChanged(object sender, bool e)
        {
            if (e)
            {
                ButtonVisibility = Visibility.Visible;
                CanUserAddRows = false;
                IsEditTable = true;
            }
            else
            {
                ButtonVisibility = Visibility.Collapsed;
                CanUserAddRows = true;
                IsEditTable = false;
            }
        }

        private void OnAddPropertyCommand(object parameter)
        {
            var error = string.Empty;
            var propertyName = parameter as string;
            var propertyExist = Properties.FirstOrDefault(x => x.Name == propertyName);
            if (propertyExist != null)
                error += "Property with this parameter already exists\n";
            if (string.IsNullOrEmpty(propertyName))
                error += "Enter the name of the property\n";
            if (SelectedType == null)
                error += "Select the type\n";
            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageBox.Show(error, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Properties.Add(new PropertyModel() { Name = propertyName, Type = SelectedType });
            DisplayDataInGrid();
        }
        public void DisplayDataInGrid()
        {
            DataTable = new DataTable();
            JArray jsonArray = new JArray();
            if (!string.IsNullOrWhiteSpace(App.jsonString))
                jsonArray = JArray.Parse(App.jsonString);
            if (jsonArray.Count > 0)
            {
                foreach (var property in Properties)
                    DataTable.Columns.Add(property.Name, property.Type);
                foreach (var jsonItem in jsonArray)
                {
                    var dataRow = DataTable.NewRow();
                    var jsonObject = jsonItem as JObject;

                    foreach (var property in jsonObject.Properties())
                    {
                        var columnName = property.Name;
                        foreach (var propertyInDynamic in Properties)
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
                foreach (var property in Properties)
                    DataTable.Columns.Add(property.Name, property.Type);
            }
            dataGrid.ItemsSource = DataTable.DefaultView;
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
            if (DataRowView == null)
            {
                MessageBox.Show("Select row in table", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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
