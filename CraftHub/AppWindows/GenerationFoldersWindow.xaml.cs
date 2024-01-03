using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace CraftHub.AppWindows
{
    /// <summary>
    /// Логика взаимодействия для RobocadExtensionWindow.xaml
    /// </summary>
    public partial class GenerationFoldersWindow : Window
    {
        List<string> folders = new List<string>() { "Python", "Java", "LabView" };
        public GenerationFoldersWindow()
        {
            InitializeComponent();
        }
        private void BGenerate_Click(object sender, RoutedEventArgs e)
        {
            string level = string.Empty;
            string blockNumber = string.Empty;
            int lessonCount = 0;
            string error = string.Empty;

            if (CBLevel.SelectedItem == null)
                error += "Select level for lesson\n";
            if (string.IsNullOrWhiteSpace(TBBlockNumber.Text))
                error += "Enter block number for lesson\n";
            if (string.IsNullOrWhiteSpace(TBLessonCount.Text))
                error += "Enter the number of lessons\n";
            if (!string.IsNullOrWhiteSpace(error))
            {
                MessageBox.Show(error, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            level = ((TextBlock)CBLevel.SelectedItem).Text;
            blockNumber = TBBlockNumber.Text;
            lessonCount = int.Parse(TBLessonCount.Text);
            var folderBrowserDialog = new FolderBrowserDialog();
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderBrowserDialog.SelectedPath;

                for (int i = 1; i <= lessonCount; i++)
                {
                    string folderName = $"{level[0]}{blockNumber}_{i}";
                    string folderPath = System.IO.Path.Combine(selectedPath, folderName);

                    // Проверяем, существует ли папка, прежде чем создавать
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                        if (IsCommon.IsChecked.Value)
                        {
                            GenerateLesson(folderPath, folderName);
                        }
                        else
                        {
                            foreach (string folder in folders)
                            {
                                string notCommonFolderPath = System.IO.Path.Combine(folderPath, folder.ToString());
                                Directory.CreateDirectory(notCommonFolderPath);
                                GenerateLesson(notCommonFolderPath, folder);
                            }
                        }

                    }

                }
            }
        }
        private void GenerateLesson(string folderPath, string folderName)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog() { Multiselect = true, Filter = $"Uploading to {folderName} | *.png; *.jpg; *.jpeg" };

            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                int countPage = 1;
                foreach (var file in openFileDialog.FileNames)
                {
                    string destinationImagePath = System.IO.Path.Combine(folderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file));
                    File.Copy(file, destinationImagePath);
                    // Добавление метаинформации для Unity
                    string metaFilePath = System.IO.Path.Combine(folderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file) + ".meta");
                    var resourceStream = Application.GetResourceStream(new Uri("Recourses/template.meta", UriKind.Relative));
                    using (var reader = new StreamReader(resourceStream.Stream))
                    {
                        string content = reader.ReadToEnd();
                        File.WriteAllText(metaFilePath, content);
                    }
                    countPage++;

                }
            }
        }
        private void TBBlockNumber_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Паттерн для проверки наличия только чисел
            string pattern = @"^\d+$";

            // Проверка с использованием Regex.IsMatch
            if (!Regex.IsMatch(e.Text, pattern))
            {
                // Если текущий ввод не соответствует паттерну, отменить ввод
                e.Handled = true;
            }
        }

        private void TBLessonCount_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Паттерн для проверки наличия только чисел
            string pattern = @"^\d+$";

            // Проверка с использованием Regex.IsMatch
            if (!Regex.IsMatch(e.Text, pattern))
            {
                // Если текущий ввод не соответствует паттерну, отменить ввод
                e.Handled = true;
            }
        }
    }

}


