using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public RobocadExtensionWindow()
        {
            InitializeComponent();
        }
        private void BGenerate_Click(object sender, RoutedEventArgs e)
        {
            var folderBrowserDialog = new FolderBrowserDialog();
            if (folderBrowserDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = folderBrowserDialog.SelectedPath;
                for (int i = 1; i <= 3; i++)
                {
                    string folderName = $"{i} folder";
                    string folderPath = System.IO.Path.Combine(selectedPath, folderName);

                    // Проверяем, существует ли папка, прежде чем создавать
                    if (!Directory.Exists(folderPath))
                    {
                        Directory.CreateDirectory(folderPath);
                    }
                }
            }
        }
    }
}
