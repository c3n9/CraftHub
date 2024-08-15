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
		public ICommand RemoveCommand { get; set; }
		public ICommand ExportCommand { get; set; }
		public ICommand ImportCommand { get; set; }
		public ICommand LoadCodeCommand { get; set; }
		public ICommand OpenGenerateFoldersWindowCommand { get; set; }
		public ICommand ExportCodeCommand { get; set; }

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


		private string _propertyName;
		public string PropertyName
		{
			get { return _propertyName; }
			set
			{
				if (_propertyName != value)
				{
					_propertyName = value;
					OnPropertyChanged(nameof(PropertyName));
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

		private DataView _dataViewTable;
		public DataView DataViewTable
		{
			get { return _dataViewTable; }
			set
			{
				_dataViewTable = value;
				OnPropertyChanged(nameof(DataViewTable));
			}
		}

		public ObservableCollection<PropertyModel> Properties { get; set; }
		public ObservableCollection<Type> AvailableTypes { get; set; }
		public DataRowView DataRowView { get; set; }
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
			RemoveCommand = new DelegateCommand(OnRemoveCammand);
			ExportCommand = new DelegateCommand(OnExportCommand);
			ImportCommand = new DelegateCommand(OnImportCommand);
			LoadCodeCommand = new DelegateCommand(OnLoadCodeCommand);
			ExportCodeCommand = new DelegateCommand(OnExportCodeCommand);
			OpenGenerateFoldersWindowCommand = new DelegateCommand(OnOpenGenerateLessonsWindowCommand);
		}

		private void OnRemoveColumnCommand(DataGridColumn column)
		{
			var table = DataTable;
			var typeName = column.SortMemberPath.ToString();
			var columnName = column.Header.ToString();

			for (int i = 0; i < table.Columns.Count; i++)
			{
				if(table.Columns[i].ColumnName == typeName)
				{
					DataTable.Columns.Remove(typeName);
				}
			} 

			// Удаляем свойство из коллекции Properties
			var property = Properties.FirstOrDefault(p => p.Name == typeName);
			if (property != null)
			{
				Properties.Remove(property);
			}

			// Удаляем колонку из DataGrid
			var columnToRemove = App.WorkingAreaView.DGJson.Columns.FirstOrDefault(c => c.Header.ToString() == columnName);
			if (columnToRemove != null)
			{
				App.WorkingAreaView.DGJson.Columns.Remove(columnToRemove);
			}

		}

		private void OnOpenGenerateLessonsWindowCommand(object paramenter)
		{
			new GenerationFoldersWinodow().ShowDialog();
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
				var selectedTabItem = App.MainWindow.TCWorkAreas.SelectedItem as TabItem;
				selectedTabItem.Header = $"{System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}";

				string code = System.IO.File.ReadAllText(dialog.FileName);
				CompileAndLoadCode(code);
				DisplayDataInGrid();
			}
		}

		private void OnExportCodeCommand(object parameter)
		{
			var dialog = new SaveFileDialog() { Filter = ".cs | *.cs" };
			if (dialog.ShowDialog().GetValueOrDefault())
			{
				var propertiesString = string.Empty;
				var properties = new List<PropertyModel>();
				
				foreach(var prop in Properties)
				{
					Type typeForToolTip = prop.Type;
					if (prop.Type.IsGenericType && prop.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
					{
						typeForToolTip = Nullable.GetUnderlyingType(prop.Type); // Получаем базовый тип
					}
					properties.Add(new PropertyModel() { Name = prop.Name, Type = typeForToolTip });
				}

				foreach(var prop in properties)
				{
					propertiesString += $"\r\n        {prop.Type.ToString()} {prop.Name} {{ get; set; }}\r\n";
				}
				var code = "using System;\r\nusing System.Collections.Generic;\r\nusing System.Linq;\r\nusing System.Text;" +
					"\r\nusing System.Threading.Tasks;\r\n\r\nnamespace YourNamespace\r\n{\r\n    " +
					$"public partial class {Path.GetFileNameWithoutExtension(dialog.FileName)}\r\n    {{\r\n    {propertiesString}    \r\n    }}\r\n}}\r\n";
				File.WriteAllText(dialog.FileName, code, Encoding.UTF8);
				var selectedTabItem = App.MainWindow.TCWorkAreas.SelectedItem as TabItem;
				selectedTabItem.Header = $"{System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}";
			}
		}

		private void OnAddPropertyCommand(object parameter)
		{
			App.jsonString = JsonConvert.SerializeObject(DataTable, Formatting.Indented);
			var error = string.Empty;
			var propertyName = PropertyName;
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
			var selectedType = SelectedType;
			if (selectedType.IsValueType)
			{
				selectedType = typeof(Nullable<>).MakeGenericType(SelectedType);
			}

			PropertyName = string.Empty;
			SelectedType = null;

			Properties.Add(new PropertyModel() { Name = propertyName, Type = selectedType });
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
					Type columnType = property.Type;

					// Проверяем, является ли тип Nullable<>
					if (property.Type.IsGenericType && property.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
					{
						columnType = Nullable.GetUnderlyingType(property.Type); // Получаем базовый тип
					}

					tempTable.Columns.Add(property.Name, columnType);
				}
			}

			// Заполняем таблицу новыми данными из JSON, если они есть
			JArray jsonArray = new JArray();
			if (!string.IsNullOrWhiteSpace(App.jsonString))
				jsonArray = JArray.Parse(App.jsonString);

			if (jsonArray.Count > 0)
			{
				foreach (var jsonItem in jsonArray)
				{
					var dataRow = tempTable.NewRow();
					var jsonObject = jsonItem as JObject;

					foreach (var property in jsonObject.Properties())
					{
						var columnName = property.Name;
						if (tempTable.Columns.Contains(columnName))
						{
							var columnValue = property.Value.ToObject<object>();
							dataRow[columnName] = columnValue ?? DBNull.Value; // Ставим DBNull если значение null
						}
					}
					tempTable.Rows.Add(dataRow);
				}
			}
			else
			{
				// Переносим данные из старой таблицы в новую только если нет новых данных из JSON
				foreach (DataRow row in DataTable.Rows)
				{
					var newRow = tempTable.NewRow();
					foreach (DataColumn column in DataTable.Columns)
					{
						newRow[column.ColumnName] = row[column];
					}
					tempTable.Rows.Add(newRow);
				}
			}

			// Обновляем DataTable на новую таблицу с сохраненными данными
			DataTable = tempTable;

			// Обновляем источник данных для DataGrid
			DataViewTable = DataTable.DefaultView;

			foreach (var column in App.WorkingAreaView.DGJson.Columns)
			{
				var property = Properties.FirstOrDefault(p => p.Name == column.Header.ToString());
				if (property != null)
				{
					var headerTemplate = new StackPanel
					{
						Orientation = Orientation.Horizontal
					};

					var headerTextBlock = new TextBlock
					{
						Text = column.Header.ToString(),
						Margin = new Thickness(0, 0, 5, 0)
					};

					// Проверяем, является ли тип Nullable<>
					Type typeForToolTip = property.Type;
					if (property.Type.IsGenericType && property.Type.GetGenericTypeDefinition() == typeof(Nullable<>))
					{
						typeForToolTip = Nullable.GetUnderlyingType(property.Type); // Получаем базовый тип
					}

					var typeIndicator = new Button
					{
						Content = new MaterialDesignThemes.Wpf.PackIcon { Kind = MaterialDesignThemes.Wpf.PackIconKind.Help },
						ToolTip = $"Type: {typeForToolTip.Name}", // Отображаем базовый тип
						Margin = new Thickness(0, 0, 5, 0),
						FontSize = 20,
						Padding = new Thickness(5),
						HorizontalContentAlignment = HorizontalAlignment.Center,
						VerticalContentAlignment = VerticalAlignment.Center,
						Style = (Style)Application.Current.Resources["ToolTipButtonStyle"]
					};

					headerTemplate.Children.Add(headerTextBlock);
					headerTemplate.Children.Add(typeIndicator);

					// Создаем контекстное меню
					var contextMenu = new ContextMenu();
					var removeMenuItem = new MenuItem { Header = "Remove Column" };
					removeMenuItem.Click += (s, e) => OnRemoveColumnCommand(column);
					contextMenu.Items.Add(removeMenuItem);

					var headerControl = new ContentControl
					{
						Content = headerTemplate,
						ContextMenu = contextMenu
					};

					column.Header = headerControl;
				}
			}
		}

		private void OnRemoveCammand(object parameter)
		{
			if (DataRowView != null)
				DataRowView.Delete();

		}
		private void OnExportCommand(object parameter)
		{
			var exportJsonString = JsonConvert.SerializeObject(DataTable, Formatting.Indented);
			var dialog = new SaveFileDialog() { Filter = ".json | *.json" };
			if (dialog.ShowDialog().GetValueOrDefault())
			{
				File.WriteAllText(dialog.FileName, exportJsonString, Encoding.UTF8);
				MessageBox.Show("Successful export", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
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
