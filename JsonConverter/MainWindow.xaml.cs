﻿using JsonConverter.Pages;
using JsonConverter.Services;
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
        
        public MainWindow()
        {
            InitializeComponent();
            MainFrame.Navigate(new PropertiesPage());


        }
        private void MIImportClass_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".cs | *.cs" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                string code = System.IO.File.ReadAllText(dialog.FileName);
                CompileAndLoadCode(code);
                MainFrame.Navigate(new PropertiesPage());
            }
        }

        private void MIImportJsonFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".json | *.json" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                GlobalSettings.jsonString = File.ReadAllText(dialog.FileName);
                MainFrame.Navigate(new JsonPage());

            }
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
                    GlobalSettings.generatedObject = instance;
                    foreach (var propertyInDynamic in instance.GetType().GetProperties())
                    {
                        var n = propertyInDynamic.Name;
                        var f = propertyInDynamic.PropertyType;
                        GlobalSettings.dictionary.Add(propertyInDynamic.Name.ToString(), propertyInDynamic.PropertyType);
                    }
                }
            }
        }

    }
}
