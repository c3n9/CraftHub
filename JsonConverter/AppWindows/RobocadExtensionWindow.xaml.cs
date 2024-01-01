using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

namespace JsonConverter.AppWindows
{
    /// <summary>
    /// Логика взаимодействия для RobocadExtensionWindow.xaml
    /// </summary>
    public partial class RobocadExtensionWindow : Window
    {
        string folderPath;
        string notCommonFolderPath;
        List<string> folders = new List<string>() { "Python", "Java", "LabView" };
        public RobocadExtensionWindow()
        {
            InitializeComponent();
        }
        private void BGenerate_Click(object sender, RoutedEventArgs e)
        {
            string level = ((TextBlock)CBLevel.SelectedItem).Text;
            string blockNumber = TBBlockNumber.Text;
            int lessonCount = int.Parse(TBLessonCount.Text);

            var folderBrowserDialog = new FolderBrowserDialog();
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderBrowserDialog.SelectedPath;

                for (int i = 1; i <= lessonCount; i++)
                {
                    string folderName = $"{level}{blockNumber}_{i}";
                    folderPath = System.IO.Path.Combine(selectedPath, folderName);

                    // Проверяем, существует ли папка, прежде чем создавать
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                        if (IsCommon.IsChecked.Value)
                        {
                            GenreateCommonLesson();
                        }
                        else
                        {
                            foreach(string folder in folders)
                            {
                                notCommonFolderPath = System.IO.Path.Combine(folderPath, folder.ToString());
                                Directory.CreateDirectory(notCommonFolderPath);
                                GenerateNotCommonLesson();
                            }
                        }

                    }

                }
            }
        }

        private void GenerateNotCommonLesson()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog() { Multiselect = true, Filter = "*.png, *.jpg; | *.png; *.jpg;" };
            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                int countPage = 1;
                foreach (var file in openFileDialog.FileNames)
                {
                    string destinationImagePath = System.IO.Path.Combine(notCommonFolderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file));
                    File.Copy(file, destinationImagePath);
                    // Добавление метаинформации для Unity
                    string metaFilePath = System.IO.Path.Combine(notCommonFolderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file) + ".meta");
                    var resourceStream = System.Windows.Application.GetResourceStream(new Uri("Recourses/template.meta", UriKind.Relative));
                    using (var reader = new StreamReader(resourceStream.Stream))
                    {
                        string content = reader.ReadToEnd();
                        File.WriteAllText(metaFilePath, content);
                    }
                    countPage++;

                }
            }
        }

        private void GenreateCommonLesson()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog() { Multiselect = true, Filter = "*.png, *.jpg; | *.png; *.jpg;" };
            if (openFileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                int countPage = 1;
                foreach (var file in openFileDialog.FileNames)
                {
                    string destinationImagePath = System.IO.Path.Combine(folderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file));
                    File.Copy(file, destinationImagePath);
                    // Добавление метаинформации для Unity
                    string metaFilePath = System.IO.Path.Combine(folderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file) + ".meta");
                    var resourceStream = System.Windows.Application.GetResourceStream(new Uri("Recourses/template.meta", UriKind.Relative));
                    using (var reader = new StreamReader(resourceStream.Stream))
                    {
                        string content = reader.ReadToEnd();
                        File.WriteAllText(metaFilePath, content);
                    }
                    countPage++;

                }
            }
        }
    }
}


