using Microsoft.CSharp;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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

namespace JsonConverter
{
    public partial class MainWindow : Window
    {
        string jsonString;
        private dynamic generatedObject;
        public MainWindow()
        {
            InitializeComponent();
        }
        private void MIImportClass_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".cs | *.cs" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                string code = System.IO.File.ReadAllText(dialog.FileName);
                CompileAndLoadCode(code);
            }
        }

        private void MIImportJsonFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".json | *.json" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                jsonString = File.ReadAllText(dialog.FileName);
                var dataTable = DisplayDataInGrid(jsonString);
                DGJsonData.ItemsSource = null;
                DGJsonData.ItemsSource = dataTable.DefaultView;
                TBJson.Text = jsonString.ToString();
            }
        }
        private DataTable DisplayDataInGrid(string json)
        {
            var dataTable = new DataTable();
            var jsonArray = JArray.Parse(json);
            if (jsonArray.Count > 0)
            {
                var properties = generatedObject.GetType().GetProperties();
                foreach (var property in properties)
                    dataTable.Columns.Add(property.Name, property.PropertyType);
                foreach (var jsonItem in jsonArray)
                {
                    var dataRow = dataTable.NewRow();
                    var jsonObject = jsonItem as JObject;

                    foreach (var property in jsonObject.Properties())
                    {
                        var columnName = property.Name;
                        foreach (var propertyInDynamic in properties)
                        {
                            // Проверяем, существует ли свойство в списке properties
                            if (propertyInDynamic.Name == property.Name)
                            {
                                var columnValue = property.Value.ToObject<object>();
                                dataRow[columnName] = columnValue;
                            }
                        }
                    }
                    dataTable.Rows.Add(dataRow);
                }
            }
            return dataTable;
        }
        private void CompileAndLoadCode(string code)
        {
            CSharpCodeProvider provider = new CSharpCodeProvider();
            CompilerParameters parameters = new CompilerParameters();
            // Получаем сборки, доступные в текущем домене приложения
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(a => a.Location).ToArray();
            parameters.ReferencedAssemblies.AddRange(assemblies);
            // Компилируем код
            CompilerResults results = provider.CompileAssemblyFromSource(parameters, code);
            if (results.Errors.HasErrors)
            {
                foreach (CompilerError error in results.Errors)
                {
                    MessageBox.Show($"Error in line {error.Line}: {error.ErrorText}");
                }
            }
            else
            {
                // Загружаем сборку
                Assembly assembly = results.CompiledAssembly;
                // Создаем экземпляры класса из сборки
                foreach (Type type in assembly.GetTypes())
                {
                    dynamic instance = Activator.CreateInstance(type);
                    generatedObject = instance;
                }
            }
        }
    }
}
