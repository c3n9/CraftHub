using JsonConverter.AppWindows;
using JsonConverter.Pages;
using JsonConverter.Services;
using Microsoft.CSharp;
using Microsoft.Win32;
using Newtonsoft.Json;
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
        public event EventHandler<bool> AddInModalWindowCheckedChanged;
        public MainWindow()
        {
            InitializeComponent();
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            GlobalSettings.mainWindow = this;
            MainFrame.Navigate(new PropertiesPage());


        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if(GlobalSettings.jsonPage != null)
                {
                    GlobalSettings.ViewSurchOption();
                    e.Handled = true;
                }
            }
        }

        private void MIImportClass_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".cs | *.cs" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                this.Title = $"CraftHub — {System.IO.Path.GetFileNameWithoutExtension(dialog.FileName)}";
                string code = System.IO.File.ReadAllText(dialog.FileName);
                CompileAndLoadCode(code);
                GlobalSettings.RefreshProperties();
            }
        }

        private void MIImportJsonFile_Click(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.dictionary.Count == 0)
            {
                MessageBox.Show("Upload a template or add properties");
                return;
            }
            var dialog = new OpenFileDialog() { Filter = ".json | *.json" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                GlobalSettings.jsonString = File.ReadAllText(dialog.FileName);
                GlobalSettings.DisplayDataInGrid();
            }
        }
        private void CompileAndLoadCode(string code)
        {
            GlobalSettings.dictionary = new Dictionary<string, dynamic>();
            GlobalSettings.jsonString = null;
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
                    GlobalSettings.generatedObject = instance;
                    foreach (var propertyInDynamic in instance.GetType().GetProperties())
                    {
                        GlobalSettings.dictionary.Add(propertyInDynamic.Name.ToString(), propertyInDynamic.PropertyType);
                    }
                }
            }
        }

        private void MIExportJsonFile_Click(object sender, RoutedEventArgs e)
        {
            var exportJsonString = JsonConvert.SerializeObject(GlobalSettings.dataTable, Formatting.Indented);
            if (!string.IsNullOrWhiteSpace(exportJsonString))
            {
                var dialog = new SaveFileDialog() { Filter = ".json | *.json" };
                if (dialog.ShowDialog().GetValueOrDefault())
                {
                    File.WriteAllText(dialog.FileName, exportJsonString, Encoding.UTF8);
                    MessageBox.Show("Successful export");
                }
            }
            else
            {
                MessageBox.Show("Import json first");
                return;
            }
        }
        private void MIAddInModalWindow_Checked(object sender, RoutedEventArgs e)
        {
            AddInModalWindowCheckedChanged?.Invoke(this, MIAddInModalWindow.IsChecked);
        }

        private void MIAddInModalWindow_Unchecked(object sender, RoutedEventArgs e)
        {
            AddInModalWindowCheckedChanged?.Invoke(this, MIAddInModalWindow.IsChecked);
        }

        private void MIViewJson_Checked(object sender, RoutedEventArgs e)
        {
            if(GlobalSettings.jsonPage != null && GlobalSettings.jsonPage.DGJsonData != null && GlobalSettings.jsonPage.DGJsonData.SelectedItem != null)
            {
                GlobalSettings.ViewJsonFromTable();
            }
        }

        private void MIViewJson_Unchecked(object sender, RoutedEventArgs e)
        {
            if (GlobalSettings.jsonPage != null && GlobalSettings.jsonPage.DGJsonData != null && GlobalSettings.jsonPage.DGJsonData.SelectedItem != null)
            {
                Grid.SetColumnSpan(GlobalSettings.jsonPage.DGJsonData, 2);
                GlobalSettings.jsonPage.TBJson.Visibility = Visibility.Collapsed;
            }
            
        }

        private void MIGenerationFolders_Click(object sender, RoutedEventArgs e)
        {
            new GenerationFoldersWindow().ShowDialog();
        }
    }
}
