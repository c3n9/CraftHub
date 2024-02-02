using CraftHub.Services;
using Microsoft.Win32;
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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace CraftHub.AppWindows
{
    /// <summary>
    /// Логика взаимодействия для LessonConstructorWindow.xaml
    /// </summary>
    public partial class LessonConstructorWindow : Window
    {
        private bool isDragging = false;
        private bool isResizing = false;
        private Point resizeStartPoint;
        private double originalWidth;
        private double originalHeight;
        public LessonConstructorWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Обработчик события нажатия левой кнопки мыши для начала перетаскивания.
        /// </summary>
        private void Dragging_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Проверяем, была ли нажата клавиша Shift при нажатии левой кнопки мыши.
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                isDragging = true;
                // Захватываем мышь, чтобы отслеживать движение, когда кнопка мыши удерживается.
                (sender as UIElement).CaptureMouse();
            }
        }

        /// <summary>
        /// Обработчик события отпускания левой кнопки мыши для окончания перетаскивания.
        /// </summary>
        private void Dragging_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Завершаем перетаскивание при отпускании левой кнопки мыши.
            isDragging = false;
            (sender as UIElement).ReleaseMouseCapture();
        }

        /// <summary>
        /// Обработчик события перемещения мыши для обновления положения элемента во время перетаскивания.
        /// </summary>
        private void Dragging_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                // Вычисляем новые координаты для элемента в зависимости от текущего положения мыши.
                double mouseX = e.GetPosition(this).X - ((UIElement)sender).RenderSize.Width / 2;
                double mouseY = e.GetPosition(this).Y - ((UIElement)sender).RenderSize.Height / 2;

                // Устанавливаем новые координаты элемента.
                Canvas.SetLeft((UIElement)sender, mouseX);
                Canvas.SetTop((UIElement)sender, mouseY);
            }
        }
        /// <summary>
        /// Обработчик события нажатия правой кнопки мыши для изменения размера.
        /// </summary>
        private void Resizing_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Устанавливаем флаг, указывающий, что начат процесс изменения размера.
            isResizing = true;

            // Запоминаем начальную позицию, где началось изменение размера.
            resizeStartPoint = e.GetPosition(sender as UIElement);

            // Запоминаем оригинальную ширину и высоту UIElement.
            originalWidth = (sender as UIElement).RenderSize.Width;
            originalHeight = (sender as UIElement).RenderSize.Height;

            // Захватываем мышь для отслеживания последующих движений мыши.
            (sender as UIElement).CaptureMouse();
        }
        /// <summary>
        /// Обработчик события отпускания правой кнопки мыши после изменения размера.
        /// </summary>
        private void Resizing_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Сбрасываем флаг изменения размера, так как изменение завершено.
            isResizing = false;

            // Освобождаем захват мыши, чтобы прекратить отслеживание дальнейших движений мыши.
            (sender as UIElement).ReleaseMouseCapture();
        }
        /// <summary>
        /// Обработчик события перемещения мыши во время изменения размера.
        /// </summary>
        private void Resizing_MouseMove(object sender, MouseEventArgs e)
        {
            // Проверяем, идет ли в данный момент процесс изменения размера.
            if (isResizing)
            {
                // Вычисляем изменение позиции по X и Y относительно начальной точки.
                double deltaX = e.GetPosition(CWorkingArea).X - resizeStartPoint.X;
                double deltaY = e.GetPosition(CWorkingArea).Y - resizeStartPoint.Y;

                // Вычисляем новую ширину и высоту на основе изменений.
                double newWidth = Math.Max(0, originalWidth + deltaX);
                double newHeight = Math.Max(0, originalHeight + deltaY);

                // Обновляем ширину и минимальную высоту изменяемого FrameworkElement.
                (sender as FrameworkElement).Width = newWidth;
                (sender as FrameworkElement).MinHeight = newHeight;
            }
        }
        private void BAddImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog() { Filter = ".png, .jpg, .jpeg | *.png; *.jpg; *.jpeg;" };
            if (dialog.ShowDialog().GetValueOrDefault())
            {
                // Создаем контекстное меню
                ContextMenu contextMenu = new ContextMenu();
                // Создаем пункт меню "Удалить"
                MenuItem deleteMenuItem = new MenuItem();
                deleteMenuItem.Header = "Delete";
                Image iconImage = new Image();
                iconImage.Source = new BitmapImage(new Uri("/Recourses/delete.png", UriKind.Relative));
                deleteMenuItem.Icon = iconImage;
                deleteMenuItem.Click += MIDelete_Click;
                // Добавляем пункт меню в контекстное меню
                contextMenu.Items.Add(deleteMenuItem);

                var photoInBytes = File.ReadAllBytes(dialog.FileName);
                Image image = new Image() 
                { 
                    Source = MyTools.BytesToImage(photoInBytes), 
                    Width = 100, 
                    Height = 100, 
                    ContextMenu = contextMenu 
                };
                image.MouseLeftButtonDown += Dragging_MouseLeftButtonDown;
                image.MouseLeftButtonUp += Dragging_MouseLeftButtonUp;
                image.MouseMove += Dragging_MouseMove;

                image.MouseRightButtonDown += Resizing_MouseRightButtonDown;
                image.MouseRightButtonUp += Resizing_MouseRightButtonUp;
                image.MouseMove += Resizing_MouseMove;

                CWorkingArea.Children.Add(image);
            }
        }

        private void BAddTextBox_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TBLabel.Text))
            {
                MessageBox.Show("Enter text", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Создаем контекстное меню
            ContextMenu contextMenu = new ContextMenu();
            // Создаем пункт меню "Удалить"
            MenuItem deleteMenuItem = new MenuItem();
            deleteMenuItem.Header = "Delete";
            Image iconImage = new Image();
            iconImage.Source = new BitmapImage(new Uri("/Recourses/delete.png", UriKind.Relative));
            deleteMenuItem.Icon = iconImage;
            deleteMenuItem.Click += MIDelete_Click;
            // Добавляем пункт меню в контекстное меню
            contextMenu.Items.Add(deleteMenuItem);

            var textBlock = new TextBlock() 
            { 
                Width = 200, 
                Height = 100, 
                Text = $"{TBLabel.Text}", 
                TextWrapping = TextWrapping.Wrap, 
                ContextMenu = contextMenu
            };
            textBlock.MouseLeftButtonDown += Dragging_MouseLeftButtonDown;
            textBlock.MouseLeftButtonUp += Dragging_MouseLeftButtonUp;
            textBlock.MouseMove += Dragging_MouseMove;

            textBlock.MouseRightButtonDown += Resizing_MouseRightButtonDown;
            textBlock.MouseRightButtonUp += Resizing_MouseRightButtonUp;
            textBlock.MouseMove += Resizing_MouseMove;

            CWorkingArea.Children.Add(textBlock);
        }
        private void MIDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ContextMenu contextMenu = menuItem.Parent as ContextMenu;
                if (contextMenu.PlacementTarget is Image image)
                    CWorkingArea.Children.Remove(image);
                if (contextMenu.PlacementTarget is TextBlock textBlock)
                    CWorkingArea.Children.Remove(textBlock);
            }
        }

        private void BClear_Click(object sender, RoutedEventArgs e)
        {
            CWorkingArea.Children.Clear();
        }

        private void AutoArrangeText()
        {
            double canvasWidth = CWorkingArea.ActualWidth;

            List<ElementInfo> elementsInfo = new List<ElementInfo>();

            foreach (UIElement element in CWorkingArea.Children)
            {
                double elementLeft = Canvas.GetLeft(element);
                double elementTop = Canvas.GetTop(element);

                if (element is Image)
                {
                    double imageWidth = ((Image)element).ActualWidth;
                    double imageHeight = ((Image)element).ActualHeight;

                    elementsInfo.Add(new ElementInfo(element, ElementType.Image, elementLeft, elementTop, imageWidth, imageHeight));
                }
                else if (element is TextBlock)
                {
                    ((TextBlock)element).Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double textWidth = Math.Min(((TextBlock)element).DesiredSize.Width, canvasWidth);
                    double textHeight = Math.Min(((TextBlock)element).DesiredSize.Height, canvasWidth);

                    elementsInfo.Add(new ElementInfo(element, ElementType.Text, elementLeft, elementTop, textWidth, textHeight));
                }
            }

            elementsInfo.Sort((a, b) => a.Top.CompareTo(b.Top));

            double currentTop = 10;
            double currentLeft = 10;

            foreach (var elementInfo in elementsInfo)
            {
                if (currentLeft + elementInfo.Width > canvasWidth)
                {
                    currentLeft = 10;
                    currentTop += elementInfo.Height + 10;
                }

                Canvas.SetLeft(elementInfo.Element, currentLeft);
                Canvas.SetTop(elementInfo.Element, currentTop);

                currentLeft += elementInfo.Width + 10;
            }
        }

        private class ElementInfo
        {
            public UIElement Element { get; }
            public ElementType Type { get; }
            public double Left { get; }
            public double Top { get; }
            public double Width { get; }
            public double Height { get; }

            public ElementInfo(UIElement element, ElementType type, double left, double top, double width, double height)
            {
                Element = element;
                Type = type;
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }
        }

        private enum ElementType
        {
            Image,
            Text
        }

        //private bool AreRectanglesIntersecting(double left1, double top1, double width1, double height1, double left2, double top2, double width2, double height2)
        //{
        //    return (left1 < left2 + width2 &&
        //            left1 + width1 > left2 &&
        //            top1 < top2 + height2 &&
        //            top1 + height1 > top2);
        //}

        private void BSave_Click(object sender, RoutedEventArgs e)
        {
            AutoArrangeText();

            double specifiedWidth = double.Parse(TBWidth.Text);
            double specifiedHeight = double.Parse(TBHeight.Text);

            CWorkingArea.Measure(new Size(specifiedWidth, specifiedHeight));
            CWorkingArea.Arrange(new Rect(new Size(specifiedWidth, specifiedHeight)));

            RenderTargetBitmap rtb = new RenderTargetBitmap((int)specifiedWidth, (int)specifiedHeight, 96, 96, PixelFormats.Default);
            rtb.Render(CWorkingArea);

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "PNG Files (*.png)|*.png";
            if (saveFileDialog.ShowDialog() == true)
            {
                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using (var fileStream = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
        }
    }
}
