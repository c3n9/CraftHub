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
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.Reflection;

namespace CraftHub.ViewModels
{
    internal class WorkingAreaViewModel : BaseViewModel
    {
        public ICommand AddPropertyCommand { get; private set; }
        public ICommand AddCommand { get; set; }
        public ICommand EditCommand { get; set; }
        public ICommand RemoveCommand { get; set; }
        public ICommand ExportCommand { get; set; }
        public ICommand ImportCommand { get; set; }
        public ICommand LoadCodeCommand { get; set; }

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
                typeof(decimal),
                typeof(byte),
                typeof(short),
                typeof(char),
            };

            AddPropertyCommand = new DelegateCommand(OnAddPropertyCommand);
            AddCommand = new DelegateCommand(OnAddCommand);
            EditCommand = new DelegateCommand(OnEditCommand);
            RemoveCommand = new DelegateCommand(OnRemoveCammand);
            ExportCommand = new DelegateCommand(OnExportCommand);
            ImportCommand = new DelegateCommand(OnImportCommand);
            LoadCodeCommand = new DelegateCommand(OnLoadCodeCommand);
            dataGrid = new DataGrid()
            {
                ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star),
                AutoGenerateColumns = true,
                FontSize = 18,
                CanUserAddRows = true,
                IsReadOnly = false,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            dataGrid.SetBinding(DataGrid.SelectedItemProperty, new Binding("DataRowView"));
            dataGrid.DataContext = this;

            UIElemetsCollection.Add(dataGrid);


        }

		private void CompileAndLoadCode(string code)
		{
			// Используем провайдер компиляции C# кода
			CSharpCodeProvider provider = new CSharpCodeProvider();
			CompilerParameters parameters = new CompilerParameters();
			// Получаем сборки, доступные в текущем домене приложения
			var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.Location).ToArray();
			parameters.ReferencedAssemblies.AddRange(assemblies);
			parameters.ReferencedAssemblies.Add("System.Runtime.dll");
			// Компилируем код
			CompilerResults results = provider.CompileAssemblyFromSource(parameters, code);
			string errorMessage = string.Empty;
			if (results.Errors.HasErrors)
			{
				foreach (CompilerError error in results.Errors)
				{
					errorMessage += $"Error in line {error.Line}: {error.ErrorText}\n";
				}
				if (!string.IsNullOrWhiteSpace(errorMessage))
				{
					MessageBox.Show(errorMessage);
					return;
				}
			}
			else
			{
				// Загружаем сборку
				Assembly assembly = results.CompiledAssembly;
				foreach (Type type in assembly.GetTypes())
				{
					dynamic instance = Activator.CreateInstance(type);
					foreach (var propertyInDynamic in instance.GetType().GetProperties())
					{
						Properties.Add(new PropertyModel() { Name = propertyInDynamic.Name, Type = propertyInDynamic.PropertyType });
					}
				}
			}
		}

		private void OnLoadCodeCommand(object parameter)
		{
			var dialog = new OpenFileDialog() { Filter = ".cs | *.cs" };
			if (dialog.ShowDialog().GetValueOrDefault())
			{
				string code = System.IO.File.ReadAllText(dialog.FileName);
				CompileAndLoadCode(code);
				DisplayDataInGrid();
			}
		}

		private void OnAddPropertyCommand(object parameter)
        {
            App.jsonString = JsonConvert.SerializeObject(DataTable, Formatting.Indented);
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
			// Создаем временную таблицу для хранения новых данных
			DataTable tempTable = new DataTable();

			// Переносим существующие столбцы в новую таблицу
			foreach (DataColumn column in DataTable.Columns)
			{
				tempTable.Columns.Add(column.ColumnName, column.DataType);
			}

			// Добавляем новые столбцы, если такие появились
			foreach (var property in Properties)
			{
				if (!tempTable.Columns.Contains(property.Name))
				{
					tempTable.Columns.Add(property.Name, property.Type);

					// Добавляем значение по умолчанию для новых столбцов
					foreach (DataRow row in tempTable.Rows)
					{
						row[property.Name] = GetDefaultValue(property.Type);
					}
				}
			}

			// Переносим данные из старой таблицы в новую
			foreach (DataRow row in DataTable.Rows)
			{
				var newRow = tempTable.NewRow();
				foreach (DataColumn column in DataTable.Columns)
				{
					newRow[column.ColumnName] = row[column];
				}
				tempTable.Rows.Add(newRow);
			}

			// Обновляем DataTable на новую таблицу с сохраненными данными
			DataTable = tempTable;

			// Заполняем таблицу новыми данными из JSON, если они есть
			JArray jsonArray = new JArray();
			if (!string.IsNullOrWhiteSpace(App.jsonString))
				jsonArray = JArray.Parse(App.jsonString);

			if (jsonArray.Count > 0)
			{
				foreach (var jsonItem in jsonArray)
				{
					var dataRow = DataTable.NewRow();
					var jsonObject = jsonItem as JObject;

					foreach (var property in jsonObject.Properties())
					{
						var columnName = property.Name;
						if (DataTable.Columns.Contains(columnName))
						{
							var columnValue = property.Value.ToObject<object>();
							dataRow[columnName] = columnValue ?? GetDefaultValue(DataTable.Columns[columnName].DataType);
						}
					}
					DataTable.Rows.Add(dataRow);
				}
			}

			// Обновляем источник данных для DataGrid
			dataGrid.ItemsSource = DataTable.DefaultView;
		}

		private object GetDefaultValue(Type type)
		{
			if (type == typeof(int))
				return 0;
			if (type == typeof(double))
				return 0.0;
			if (type == typeof(float))
				return 0.0f;
			if (type == typeof(decimal))
				return 0m;
			if (type == typeof(byte))
				return (byte)0;
			if (type == typeof(short))
				return (short)0;
			if (type == typeof(bool))
				return false;
			if (type == typeof(char))
				return '\0';

			return DBNull.Value;
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
            if (!string.IsNullOrWhiteSpace(exportJsonString) && exportJsonString != "[]")
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

		private void OnImportCommand(object parameter)
		{
			if (Properties.Count == 0)
			{
				MessageBox.Show("Upload a template or add properties");
				return;
			}
			var dialog = new OpenFileDialog() { Filter = ".json | *.json" };
			if (dialog.ShowDialog().GetValueOrDefault())
			{
				App.jsonString = File.ReadAllText(dialog.FileName);
				DisplayDataInGrid();
			}
		}
	}
}
