using CraftHub.ViewModels.Base;
using CraftHub.ViewModels.Commands;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CraftHub.ViewModels
{
    internal class GenerationFoldersWinodowViewModel : BaseViewModel
    {
        public ICommand CloseWindowCommand { get; set; }
        public ICommand GenerateFoldersCommand { get; set; }

        public string Level { get; set; }
        private bool _isCommon;
        public bool IsCommon
        {
            get
            {
                return _isCommon;
            }
            set
            {
                _isCommon = value;
                OnPropertyChanged();
            }
        }
        private int _blockNumber;
        public int BlockNumber
        {
            get
            {
                return _blockNumber;
            }
            set
            {
                _blockNumber = value;
                OnPropertyChanged();
            }
        }
        private int _lessonCount;
        public int LessonCount
        {
            get
            {
                return _lessonCount;
            }
            set
            {
                _lessonCount = value;
                OnPropertyChanged();
            }
        }
        private List<string> folders = new List<string>() { "Python", "Java", "LabView" };

        public GenerationFoldersWinodowViewModel()
        {
            _isCommon = true;

            CloseWindowCommand = new DelegateCommand(OnCloseWindowCommand);
            GenerateFoldersCommand = new DelegateCommand(OnGenerateFoldersCommand);
        }
        private void OnCloseWindowCommand(object paramenter)
        {
            (paramenter as Window).Close();
        }
        private void OnGenerateFoldersCommand(object paramenter)
        {
            string level = string.Empty;
            int blockNumber = 0;
            int lessonCount = 0;
            string error = string.Empty;

            if (Level == null)
                error += "Select level for lesson\n";
            if (string.IsNullOrWhiteSpace(BlockNumber.ToString()))
                error += "Enter block number for lesson\n";
            if (string.IsNullOrWhiteSpace(LessonCount.ToString()))
                error += "Enter the number of lessons\n";
            if (LessonCount <= 0)
                error += "Enter the number of lessons greater than 0\n";
            if (LessonCount <= 0)
                error += "Enter the number of block greater than 0\n";
            if (!string.IsNullOrWhiteSpace(error))
            {
                System.Windows.MessageBox.Show(error, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            level = Level;
            blockNumber = BlockNumber;
            lessonCount = LessonCount;
            var folderBrowserDialog = new System.Windows.Forms.FolderBrowserDialog();
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
                        if (IsCommon)
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
                        System.Windows.MessageBox.Show("Succesful generation folders");

                    }

                }
            }
        }
        private void GenerateLesson(string folderPath, string folderName)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog() { Multiselect = true, Filter = $"Uploading to {folderName} | *.png; *.jpg; *.jpeg" };

            if (openFileDialog.ShowDialog().GetValueOrDefault())
            {
                int countPage = 1;
                foreach (var file in openFileDialog.FileNames)
                {
                    string destinationImagePath = System.IO.Path.Combine(folderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file));
                    File.Copy(file, destinationImagePath);
                    string metaFilePath = System.IO.Path.Combine(folderPath, $"Page{countPage}" + System.IO.Path.GetExtension(file) + ".meta");
                    var resourceStream = System.Windows.Application.GetResourceStream(new Uri("Resources/template.meta", UriKind.Relative));
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
