using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Paint1
{
    public enum ResizeMarkerType
    {
        TopLeft,
        TopMiddle,
        TopRight,
        MiddleLeft,
        MiddleRight,
        BottomLeft,
        BottomMiddle,
        BottomRight
    }

    public class ResizeMarker
    {
        public Rectangle Rectangle { get; private set; }
        public ResizeMarkerType Type { get; private set; }
        private double baseSize = 10; // Базовый размер маркера

        public ResizeMarker(ResizeMarkerType type)
        {
            Type = type;

            Rectangle = new Rectangle();

            // Делаем маркер круглым
            Rectangle.RadiusX = 5;
            Rectangle.RadiusY = 5;

            // Настраиваем внешний вид
            UpdateSize(1.0); // Начальный размер
            Rectangle.Fill = Brushes.White;
            Rectangle.Stroke = Brushes.Blue;
            Rectangle.StrokeThickness = 2;

            // Устанавливаем курсор в зависимости от типа маркера
            Rectangle.Cursor = GetCursorForMarker(type);
        }

        public void UpdateSize(double scaleFactor)
        {
            // Размер маркера обратно пропорционален масштабу
            double size = Math.Max(5, baseSize / scaleFactor);
            Rectangle.Width = size;
            Rectangle.Height = size;
            Rectangle.RadiusX = size / 2;
            Rectangle.RadiusY = size / 2;
        }

        private Cursor GetCursorForMarker(ResizeMarkerType type)
        {
            switch (type)
            {
                case ResizeMarkerType.TopLeft:
                case ResizeMarkerType.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeMarkerType.TopRight:
                case ResizeMarkerType.BottomLeft:
                    return Cursors.SizeNESW;
                case ResizeMarkerType.TopMiddle:
                case ResizeMarkerType.BottomMiddle:
                    return Cursors.SizeNS;
                case ResizeMarkerType.MiddleLeft:
                case ResizeMarkerType.MiddleRight:
                    return Cursors.SizeWE;
                default:
                    return Cursors.Arrow;
            }
        }
    }

    public enum ShapeType
    {
        Line,
        Rectangle,
        Ellipse,
        Polygon
    }

    public class ShapeInfo
    {
        public ShapeType Type { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        public List<Point> Points { get; set; } = new List<Point>();
        public Color StrokeColor { get; set; } = Colors.Black;
        public Color FillColor { get; set; } = Colors.Transparent;
        public double StrokeThickness { get; set; } = 2;
    }

    public partial class MainWindow : Window
    {
        private bool isDrawing = false;
        private Point startPoint;
        private Shape currentShape;
        private string selectedTool = "Line";
        private double hue = 0;
        private double saturation = 0;
        private double value = 0;
        private Color currentStrokeColor = Colors.Black;
        private Color currentFillColor = Colors.Black;
        private bool isStrokeMode = true;
        private bool isDraggingMarker = false;
        private WriteableBitmap svBitmap;
        private Image svImage;
        private Shape selectedShape = null;
        private Dictionary<Shape, (Brush originalStroke, double originalStrokeThickness, Brush originalFill)> shapeOriginals =
            new Dictionary<Shape, (Brush, double, Brush)>();
        private bool isMoving = false;
        private Point lastMousePosition;
        private bool isDrawingPolygon = false;
        private List<Point> polygonPoints = new List<Point>();
        private Polygon currentPolygon = null;
        private List<Shape> undoStack = new List<Shape>();
        private List<Shape> redoStack = new List<Shape>();
        private double minScale = 0.1;
        private double maxScale = 2.0;
        private double currentScale = 1.0;

        // Новые поля для масштабирования фигур
        private List<ResizeMarker> resizeMarkers = new List<ResizeMarker>();
        private bool isResizing = false;
        private ResizeMarker activeResizeMarker = null;
        private Point resizeStartPoint;
        private Rect originalBounds;

        // Новые поля для работы с SVG
        private List<ShapeInfo> shapeInfos = new List<ShapeInfo>();
        private bool isProcessingFile = false; // Флаг для предотвращения двойных сообщений

        public MainWindow()
        {
            InitializeComponent();

            Canvas1.Width = 5000;
            Canvas1.Height = 5000;

            Canvas1.LayoutTransform = new ScaleTransform(currentScale, currentScale);

            Canvas1.MouseDown += Canvas_MouseDown;
            Canvas1.MouseMove += Canvas_MouseMove;
            Canvas1.MouseUp += Canvas_MouseUp;
            Canvas1.MouseRightButtonDown += Canvas_MouseRightButtonDown;

            // Обработчики для кнопок инструментов
            LineButton.Click += (s, e) =>
            {
                selectedTool = "Line";
                Deselect();
            };

            SquareButton.Click += (s, e) =>
            {
                selectedTool = "Square";
                Deselect();
            };

            EllipseButton.Click += (s, e) =>
            {
                selectedTool = "Ellipse";
                Deselect();
            };

            PolygonButton.Click += (s, e) =>
            {
                selectedTool = "Polygon";
                Deselect();
            };

            // Обработчики для кнопок файлов
            NewButton.Click += NewButton_Click;
            OpenButton.Click += OpenButton_Click;
            SaveButton.Click += SaveButton_Click;

            ZoomSlider.Minimum = minScale;
            ZoomSlider.Maximum = maxScale;
            ZoomSlider.Value = 1.0;
            ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;

            ZoomResetButton.Click += ZoomReset_Click;

            double[] hsv = RGBtoHSV(currentStrokeColor);
            hue = hsv[0];
            saturation = hsv[1];
            value = hsv[2];

            HueSlider.Value = hue;

            svBitmap = new WriteableBitmap(150, 150, 96, 96, PixelFormats.Bgr32, null);
            svImage = new Image { Source = svBitmap, Width = 150, Height = 150 };
            SVSquare.Children.Add(svImage);

            LinearGradientBrush hueBrush = new LinearGradientBrush();
            hueBrush.StartPoint = new Point(0.5, 0);
            hueBrush.EndPoint = new Point(0.5, 1);
            hueBrush.GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(255, 0, 0), 0.0 / 360.0),
                new GradientStop(Color.FromRgb(255, 255, 0), 60.0 / 360.0),
                new GradientStop(Color.FromRgb(0, 255, 0), 120.0 / 360.0),
                new GradientStop(Color.FromRgb(0, 255, 255), 180.0 / 360.0),
                new GradientStop(Color.FromRgb(0, 0, 255), 240.0 / 360.0),
                new GradientStop(Color.FromRgb(255, 0, 255), 300.0 / 360.0),
                new GradientStop(Color.FromRgb(255, 0, 0), 360.0 / 360.0)
            };
            HueSlider.Background = hueBrush;

            StrokePreview.MouseLeftButtonDown += StrokePreview_MouseLeftButtonDown;
            FillPreview.MouseLeftButtonDown += FillPreview_MouseLeftButtonDown;

            HueSlider.ValueChanged += HueSlider_ValueChanged;

            this.KeyDown += MainWindow_KeyDown;

            UpdateColorPreviews();
            UpdateZoomText();
            UpdateSVSquare();
            UpdateSVMarkerPosition();
            UpdateColor();

            MainScrollViewer.PreviewMouseWheel += Canvas_MouseWheel;
        }

        // Обработчики для кнопок файлов
        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            if (isProcessingFile) return;
            NewDocument();
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (isProcessingFile) return;
            OpenSVG();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (isProcessingFile) return;
            SaveSVG();
        }

        private void NewDocument()
        {
            if (MessageBox.Show("Создать новый документ? Все несохраненные изменения будут потеряны.",
                "Новый документ", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Canvas1.Children.Clear();
                shapeOriginals.Clear();
                undoStack.Clear();
                redoStack.Clear();
                shapeInfos.Clear();
                Deselect();
            }
        }

        private void SaveSVG()
        {
            isProcessingFile = true;

            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "SVG Files (*.svg)|*.svg|All Files (*.*)|*.*",
                DefaultExt = ".svg",
                FileName = "drawing.svg"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    // Собираем информацию о всех фигурах
                    UpdateShapeInfos();

                    // Генерируем SVG
                    string svgContent = GenerateSVG();

                    // Сохраняем в файл
                    File.WriteAllText(saveDialog.FileName, svgContent, Encoding.UTF8);

                    MessageBox.Show($"Файл успешно сохранен: {saveDialog.FileName}",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при сохранении SVG: {ex.Message}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            isProcessingFile = false;
        }

        private void OpenSVG()
        {
            isProcessingFile = true;

            OpenFileDialog openDialog = new OpenFileDialog
            {
                Filter = "SVG Files (*.svg)|*.svg|All Files (*.*)|*.*",
                DefaultExt = ".svg"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    // Очищаем текущий холст
                    Canvas1.Children.Clear();
                    shapeOriginals.Clear();
                    undoStack.Clear();
                    redoStack.Clear();
                    shapeInfos.Clear();

                    // Загружаем SVG
                    LoadSVG(openDialog.FileName);

                    MessageBox.Show($"Файл успешно загружен: {openDialog.FileName}",
                        "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при загрузке SVG: {ex.Message}",
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            isProcessingFile = false;
        }

        private void UpdateShapeInfos()
        {
            shapeInfos.Clear();

            foreach (UIElement element in Canvas1.Children)
            {
                // Пропускаем маркеры изменения размера
                if (element is Rectangle rect && resizeMarkers.Any(m => m.Rectangle == rect))
                    continue;

                if (element is Shape shape)
                {
                    ShapeInfo info = new ShapeInfo();

                    // Определяем тип фигуры
                    if (shape is Line line)
                    {
                        info.Type = ShapeType.Line;
                        info.Points = new List<Point> { new Point(line.X1, line.Y1), new Point(line.X2, line.Y2) };
                    }
                    else if (shape is Rectangle rectShape)
                    {
                        // Проверяем, не является ли это маркером изменения размера
                        if (resizeMarkers.Any(m => m.Rectangle == rectShape))
                            continue;

                        info.Type = ShapeType.Rectangle;
                        info.X = Canvas.GetLeft(rectShape);
                        info.Y = Canvas.GetTop(rectShape);
                        info.Width = rectShape.Width;
                        info.Height = rectShape.Height;
                        info.RadiusX = rectShape.RadiusX;
                        info.RadiusY = rectShape.RadiusY;
                    }
                    else if (shape is Ellipse ellipse)
                    {
                        info.Type = ShapeType.Ellipse;
                        info.X = Canvas.GetLeft(ellipse);
                        info.Y = Canvas.GetTop(ellipse);
                        info.Width = ellipse.Width;
                        info.Height = ellipse.Height;
                    }
                    else if (shape is Polygon polygon)
                    {
                        info.Type = ShapeType.Polygon;
                        info.Points = polygon.Points.ToList();
                    }
                    else
                    {
                        continue; // Пропускаем неизвестные фигуры
                    }

                    // Получаем цвета
                    if (shape.Stroke is SolidColorBrush strokeBrush)
                    {
                        info.StrokeColor = strokeBrush.Color;
                        info.StrokeThickness = shape.StrokeThickness;
                    }
                    else
                    {
                        info.StrokeColor = Colors.Black;
                        info.StrokeThickness = 2;
                    }

                    if (shape.Fill is SolidColorBrush fillBrush)
                    {
                        info.FillColor = fillBrush.Color;
                    }
                    else if (shape.Fill == Brushes.Transparent || shape.Fill == null)
                    {
                        info.FillColor = Colors.Transparent;
                    }
                    else
                    {
                        info.FillColor = Colors.Transparent;
                    }

                    shapeInfos.Add(info);
                }
            }
        }

        private string GenerateSVG()
        {
            StringBuilder svgBuilder = new StringBuilder();

            // Заголовок SVG
            svgBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
            svgBuilder.AppendLine($"<svg width=\"{Canvas1.Width}\" height=\"{Canvas1.Height}\" ");
            svgBuilder.AppendLine("     xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\">");
            svgBuilder.AppendLine("  <title>Paint App Drawing</title>");
            svgBuilder.AppendLine("  <desc>Drawing created with Paint App</desc>");

            // Добавляем каждую фигуру
            foreach (ShapeInfo info in shapeInfos)
            {
                string shapeSvg = ShapeToSVG(info);
                if (!string.IsNullOrEmpty(shapeSvg))
                {
                    svgBuilder.AppendLine(shapeSvg);
                }
            }

            svgBuilder.AppendLine("</svg>");

            return svgBuilder.ToString();
        }

        private string ShapeToSVG(ShapeInfo info)
        {
            StringBuilder shapeSVG = new StringBuilder();

            // Общие атрибуты
            string strokeColor = ColorToHex(info.StrokeColor);
            string fillColor = info.FillColor == Colors.Transparent ? "none" : ColorToHex(info.FillColor);

            switch (info.Type)
            {
                case ShapeType.Line:
                    if (info.Points.Count >= 2)
                    {
                        shapeSVG.Append($"  <line x1=\"{info.Points[0].X:F2}\" y1=\"{info.Points[0].Y:F2}\" ");
                        shapeSVG.Append($"x2=\"{info.Points[1].X:F2}\" y2=\"{info.Points[1].Y:F2}\" ");
                        shapeSVG.Append($"stroke=\"{strokeColor}\" stroke-width=\"{info.StrokeThickness:F2}\" fill=\"none\"/>");
                    }
                    break;

                case ShapeType.Rectangle:
                    string rxAttr = info.RadiusX > 0 ? $" rx=\"{info.RadiusX:F2}\"" : "";
                    string ryAttr = info.RadiusY > 0 ? $" ry=\"{info.RadiusY:F2}\"" : "";

                    shapeSVG.Append($"  <rect x=\"{info.X:F2}\" y=\"{info.Y:F2}\" ");
                    shapeSVG.Append($"width=\"{info.Width:F2}\" height=\"{info.Height:F2}\"{rxAttr}{ryAttr} ");
                    shapeSVG.Append($"stroke=\"{strokeColor}\" stroke-width=\"{info.StrokeThickness:F2}\" ");
                    shapeSVG.Append($"fill=\"{fillColor}\"/>");
                    break;

                case ShapeType.Ellipse:
                    double cx = info.X + info.Width / 2;
                    double cy = info.Y + info.Height / 2;
                    double rx = info.Width / 2;
                    double ry = info.Height / 2;

                    shapeSVG.Append($"  <ellipse cx=\"{cx:F2}\" cy=\"{cy:F2}\" ");
                    shapeSVG.Append($"rx=\"{rx:F2}\" ry=\"{ry:F2}\" ");
                    shapeSVG.Append($"stroke=\"{strokeColor}\" stroke-width=\"{info.StrokeThickness:F2}\" ");
                    shapeSVG.Append($"fill=\"{fillColor}\"/>");
                    break;

                case ShapeType.Polygon:
                    if (info.Points.Count > 0)
                    {
                        string pointsStr = string.Join(" ", info.Points.Select(p => $"{p.X:F2},{p.Y:F2}"));
                        shapeSVG.Append($"  <polygon points=\"{pointsStr}\" ");
                        shapeSVG.Append($"stroke=\"{strokeColor}\" stroke-width=\"{info.StrokeThickness:F2}\" ");
                        shapeSVG.Append($"fill=\"{fillColor}\"/>");
                    }
                    break;
            }

            return shapeSVG.ToString();
        }

        private string ColorToHex(Color color)
        {
            if (color.A < 255)
            {
                return $"rgba({color.R},{color.G},{color.B},{color.A / 255.0:F2})";
            }
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private void LoadSVG(string filePath)
        {
            try
            {
                // Читаем SVG файл
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(filePath);

                // Находим все элементы фигур
                XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
                nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");

                // Обрабатываем линии
                XmlNodeList lines = xmlDoc.SelectNodes("//svg:line", nsmgr);
                if (lines != null)
                {
                    foreach (XmlNode lineNode in lines)
                    {
                        LoadSVGLine(lineNode);
                    }
                }

                // Обрабатываем прямоугольники
                XmlNodeList rects = xmlDoc.SelectNodes("//svg:rect", nsmgr);
                if (rects != null)
                {
                    foreach (XmlNode rectNode in rects)
                    {
                        LoadSVGRectangle(rectNode);
                    }
                }

                // Обрабатываем эллипсы
                XmlNodeList ellipses = xmlDoc.SelectNodes("//svg:ellipse", nsmgr);
                if (ellipses != null)
                {
                    foreach (XmlNode ellipseNode in ellipses)
                    {
                        LoadSVGEllipse(ellipseNode);
                    }
                }

                // Обрабатываем полигоны
                XmlNodeList polygons = xmlDoc.SelectNodes("//svg:polygon", nsmgr);
                if (polygons != null)
                {
                    foreach (XmlNode polygonNode in polygons)
                    {
                        LoadSVGPolygon(polygonNode);
                    }
                }

                // Также обрабатываем полилинии (polyline)
                XmlNodeList polylines = xmlDoc.SelectNodes("//svg:polyline", nsmgr);
                if (polylines != null)
                {
                    foreach (XmlNode polylineNode in polylines)
                    {
                        LoadSVGPolyline(polylineNode);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка парсинга SVG: {ex.Message}", ex);
            }
        }

        private void LoadSVGLine(XmlNode lineNode)
        {
            double x1 = GetDoubleAttribute(lineNode, "x1");
            double y1 = GetDoubleAttribute(lineNode, "y1");
            double x2 = GetDoubleAttribute(lineNode, "x2");
            double y2 = GetDoubleAttribute(lineNode, "y2");

            Color strokeColor = GetColorAttribute(lineNode, "stroke", Colors.Black);
            double strokeWidth = GetDoubleAttribute(lineNode, "stroke-width", 2);

            Line line = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = strokeWidth,
                Fill = Brushes.Transparent
            };

            Canvas1.Children.Add(line);
            shapeOriginals[line] = (line.Stroke, line.StrokeThickness, line.Fill);
            undoStack.Add(line);
        }

        private void LoadSVGRectangle(XmlNode rectNode)
        {
            double x = GetDoubleAttribute(rectNode, "x");
            double y = GetDoubleAttribute(rectNode, "y");
            double width = GetDoubleAttribute(rectNode, "width");
            double height = GetDoubleAttribute(rectNode, "height");
            double rx = GetDoubleAttribute(rectNode, "rx", 0);
            double ry = GetDoubleAttribute(rectNode, "ry", 0);

            Color strokeColor = GetColorAttribute(rectNode, "stroke", Colors.Black);
            Color fillColor = GetColorAttribute(rectNode, "fill", Colors.Transparent);
            double strokeWidth = GetDoubleAttribute(rectNode, "stroke-width", 2);

            Rectangle rect = new Rectangle
            {
                Width = width,
                Height = height,
                RadiusX = rx,
                RadiusY = ry,
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = strokeWidth
            };

            if (fillColor != Colors.Transparent)
            {
                rect.Fill = new SolidColorBrush(fillColor);
            }
            else
            {
                rect.Fill = Brushes.Transparent;
            }

            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);

            Canvas1.Children.Add(rect);
            shapeOriginals[rect] = (rect.Stroke, rect.StrokeThickness, rect.Fill);
            undoStack.Add(rect);
        }

        private void LoadSVGEllipse(XmlNode ellipseNode)
        {
            double cx = GetDoubleAttribute(ellipseNode, "cx");
            double cy = GetDoubleAttribute(ellipseNode, "cy");
            double rx = GetDoubleAttribute(ellipseNode, "rx");
            double ry = GetDoubleAttribute(ellipseNode, "ry");

            Color strokeColor = GetColorAttribute(ellipseNode, "stroke", Colors.Black);
            Color fillColor = GetColorAttribute(ellipseNode, "fill", Colors.Transparent);
            double strokeWidth = GetDoubleAttribute(ellipseNode, "stroke-width", 2);

            // Преобразуем параметры эллипса в параметры WPF Ellipse
            double x = cx - rx;
            double y = cy - ry;
            double width = rx * 2;
            double height = ry * 2;

            Ellipse ellipse = new Ellipse
            {
                Width = width,
                Height = height,
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = strokeWidth
            };

            if (fillColor != Colors.Transparent)
            {
                ellipse.Fill = new SolidColorBrush(fillColor);
            }
            else
            {
                ellipse.Fill = Brushes.Transparent;
            }

            Canvas.SetLeft(ellipse, x);
            Canvas.SetTop(ellipse, y);

            Canvas1.Children.Add(ellipse);
            shapeOriginals[ellipse] = (ellipse.Stroke, ellipse.StrokeThickness, ellipse.Fill);
            undoStack.Add(ellipse);
        }

        private void LoadSVGPolygon(XmlNode polygonNode)
        {
            string pointsStr = GetStringAttribute(polygonNode, "points", "");
            if (string.IsNullOrEmpty(pointsStr)) return;

            // Парсим точки
            List<Point> points = ParsePoints(pointsStr);

            if (points.Count < 2) return;

            Color strokeColor = GetColorAttribute(polygonNode, "stroke", Colors.Black);
            Color fillColor = GetColorAttribute(polygonNode, "fill", Colors.Transparent);
            double strokeWidth = GetDoubleAttribute(polygonNode, "stroke-width", 2);

            Polygon polygon = new Polygon
            {
                Points = new PointCollection(points),
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = strokeWidth
            };

            if (fillColor != Colors.Transparent)
            {
                polygon.Fill = new SolidColorBrush(fillColor);
            }
            else
            {
                polygon.Fill = Brushes.Transparent;
            }

            Canvas1.Children.Add(polygon);
            shapeOriginals[polygon] = (polygon.Stroke, polygon.StrokeThickness, polygon.Fill);
            undoStack.Add(polygon);
        }

        private void LoadSVGPolyline(XmlNode polylineNode)
        {
            string pointsStr = GetStringAttribute(polylineNode, "points", "");
            if (string.IsNullOrEmpty(pointsStr)) return;

            // Парсим точки
            List<Point> points = ParsePoints(pointsStr);

            if (points.Count < 2) return;

            Color strokeColor = GetColorAttribute(polylineNode, "stroke", Colors.Black);
            Color fillColor = GetColorAttribute(polylineNode, "fill", Colors.Transparent);
            double strokeWidth = GetDoubleAttribute(polylineNode, "stroke-width", 2);

            // Преобразуем полилинию в полигон (замкнем ее)
            Polygon polygon = new Polygon
            {
                Points = new PointCollection(points),
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = strokeWidth
            };

            if (fillColor != Colors.Transparent)
            {
                polygon.Fill = new SolidColorBrush(fillColor);
            }
            else
            {
                polygon.Fill = Brushes.Transparent;
            }

            Canvas1.Children.Add(polygon);
            shapeOriginals[polygon] = (polygon.Stroke, polygon.StrokeThickness, polygon.Fill);
            undoStack.Add(polygon);
        }

        private List<Point> ParsePoints(string pointsStr)
        {
            List<Point> points = new List<Point>();

            // Разделяем строку по пробелам и запятым
            string[] tokens = pointsStr.Split(new char[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length; i += 2)
            {
                if (i + 1 < tokens.Length)
                {
                    if (double.TryParse(tokens[i].Replace('.', ','), out double x) &&
                        double.TryParse(tokens[i + 1].Replace('.', ','), out double y))
                    {
                        points.Add(new Point(x, y));
                    }
                }
            }

            return points;
        }

        private double GetDoubleAttribute(XmlNode node, string attrName, double defaultValue = 0)
        {
            XmlAttribute attr = node.Attributes[attrName];
            if (attr != null)
            {
                string value = attr.Value.Replace('.', ',');
                if (double.TryParse(value, out double result))
                {
                    return result;
                }
            }
            return defaultValue;
        }

        private string GetStringAttribute(XmlNode node, string attrName, string defaultValue = "")
        {
            XmlAttribute attr = node.Attributes[attrName];
            return attr?.Value ?? defaultValue;
        }

        private Color GetColorAttribute(XmlNode node, string attrName, Color defaultColor)
        {
            XmlAttribute attr = node.Attributes[attrName];
            if (attr != null)
            {
                try
                {
                    string value = attr.Value.Trim();

                    if (value == "none")
                        return Colors.Transparent;

                    if (value.StartsWith("#"))
                    {
                        string hex = value.TrimStart('#');
                        if (hex.Length == 6)
                        {
                            return Color.FromRgb(
                                Convert.ToByte(hex.Substring(0, 2), 16),
                                Convert.ToByte(hex.Substring(2, 2), 16),
                                Convert.ToByte(hex.Substring(4, 2), 16)
                            );
                        }
                        else if (hex.Length == 8)
                        {
                            return Color.FromArgb(
                                Convert.ToByte(hex.Substring(0, 2), 16),
                                Convert.ToByte(hex.Substring(2, 2), 16),
                                Convert.ToByte(hex.Substring(4, 2), 16),
                                Convert.ToByte(hex.Substring(6, 2), 16)
                            );
                        }
                    }
                    else if (value.StartsWith("rgba"))
                    {
                        // Парсим rgba(r,g,b,a)
                        string values = value.TrimStart("rgba(".ToCharArray()).TrimEnd(')');
                        string[] parts = values.Split(',');
                        if (parts.Length == 4)
                        {
                            return Color.FromArgb(
                                (byte)(double.Parse(parts[3].Replace('.', ',')) * 255),
                                byte.Parse(parts[0]),
                                byte.Parse(parts[1]),
                                byte.Parse(parts[2])
                            );
                        }
                    }
                    else if (value.StartsWith("rgb"))
                    {
                        // Парсим rgb(r,g,b)
                        string values = value.TrimStart("rgb(".ToCharArray()).TrimEnd(')');
                        string[] parts = values.Split(',');
                        if (parts.Length == 3)
                        {
                            return Color.FromRgb(
                                byte.Parse(parts[0]),
                                byte.Parse(parts[1]),
                                byte.Parse(parts[2])
                            );
                        }
                    }
                    else
                    {
                        // Попробуем распознать именованные цвета
                        switch (value.ToLower())
                        {
                            case "black": return Colors.Black;
                            case "white": return Colors.White;
                            case "red": return Colors.Red;
                            case "green": return Colors.Green;
                            case "blue": return Colors.Blue;
                            case "yellow": return Colors.Yellow;
                            case "cyan": return Colors.Cyan;
                            case "magenta": return Colors.Magenta;
                            case "gray": return Colors.Gray;
                            case "transparent": return Colors.Transparent;
                        }
                    }
                }
                catch
                {
                    // В случае ошибки возвращаем цвет по умолчанию
                }
            }
            return defaultColor;
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point clickPoint = e.GetPosition(Canvas1);

            // Проверяем, не кликнули ли мы по маркеру изменения размера
            foreach (var marker in resizeMarkers)
            {
                if (marker.Rectangle.IsMouseOver)
                {
                    activeResizeMarker = marker;
                    isResizing = true;
                    resizeStartPoint = clickPoint;

                    // Сохраняем исходные границы фигуры
                    if (selectedShape != null)
                    {
                        originalBounds = GetShapeBounds(selectedShape);
                        originalBounds.Inflate(5, 5);
                    }

                    e.Handled = true;
                    return;
                }
            }

            Shape hitShape = GetHitShape(clickPoint);

            if (hitShape != null && !isDrawingPolygon)
            {
                SelectShape(hitShape);
                isMoving = true;
                lastMousePosition = clickPoint;
            }
            else if (selectedTool == "Polygon" && e.ChangedButton == MouseButton.Left)
            {
                HandlePolygonMouseDown(clickPoint);
            }
            else
            {
                HandleDrawingStart(clickPoint);
            }
        }

        private void HandlePolygonMouseDown(Point clickPoint)
        {
            if (!isDrawingPolygon)
            {
                isDrawingPolygon = true;
                polygonPoints.Clear();
                Deselect();

                currentPolygon = new Polygon
                {
                    Stroke = new SolidColorBrush(currentStrokeColor),
                    Fill = Brushes.Transparent,
                    StrokeThickness = 2
                };
                Canvas1.Children.Add(currentPolygon);
            }
            polygonPoints.Add(clickPoint);
            UpdatePolygonPreview();
        }

        private void HandleDrawingStart(Point clickPoint)
        {
            Deselect();
            startPoint = clickPoint;
            isDrawing = true;

            switch (selectedTool)
            {
                case "Line":
                    currentShape = new Line
                    {
                        X1 = startPoint.X,
                        Y1 = startPoint.Y,
                        X2 = startPoint.X,
                        Y2 = startPoint.Y,
                        Stroke = new SolidColorBrush(currentStrokeColor),
                        StrokeThickness = 2
                    };
                    break;

                case "Square":
                    currentShape = new Rectangle
                    {
                        Stroke = new SolidColorBrush(currentStrokeColor),
                        Fill = new SolidColorBrush(currentFillColor),
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(currentShape, startPoint.X);
                    Canvas.SetTop(currentShape, startPoint.Y);
                    break;

                case "Ellipse":
                    currentShape = new Ellipse
                    {
                        Stroke = new SolidColorBrush(currentStrokeColor),
                        Fill = new SolidColorBrush(currentFillColor),
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(currentShape, startPoint.X);
                    Canvas.SetTop(currentShape, startPoint.Y);
                    break;
            }

            if (currentShape != null)
            {
                Canvas1.Children.Add(currentShape);
                shapeOriginals[currentShape] = (currentShape.Stroke, currentShape.StrokeThickness, currentShape.Fill);
            }
        }

        private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (selectedShape != null && selectedTool == "Polygon" && !isDrawingPolygon)
            {
                Deselect();
                return;
            }

            if (selectedTool == "Polygon" && isDrawingPolygon)
            {
                FinishPolygon();
            }
        }

        private void FinishPolygon()
        {
            if (polygonPoints.Count <= 2)
            {
                if (currentPolygon != null)
                {
                    Canvas1.Children.Remove(currentPolygon);
                    currentPolygon = null;
                }
            }
            else
            {
                try
                {
                    if (currentPolygon != null)
                    {
                        currentPolygon.Fill = new SolidColorBrush(currentFillColor);
                        shapeOriginals[currentPolygon] = (currentPolygon.Stroke, currentPolygon.StrokeThickness, currentPolygon.Fill);
                    }

                    if (undoStack.Count >= 5)
                        undoStack.RemoveAt(0);

                    undoStack.Add(currentPolygon);

                    if (currentPolygon != null)
                        SelectShape(currentPolygon);

                    currentPolygon = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при завершении полигона: {ex.Message}");

                    if (currentPolygon != null)
                        Canvas1.Children.Remove(currentPolygon);
                }
            }

            isDrawingPolygon = false;
            polygonPoints.Clear();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentPoint = e.GetPosition(Canvas1);

            if (isResizing && selectedShape != null && activeResizeMarker != null)
            {
                ResizeSelectedShape(currentPoint);
            }
            else if (selectedShape != null && isMoving)
            {
                MoveSelectedShape(currentPoint);
            }
            else if (isDrawingPolygon)
            {
                UpdatePolygonPreview(currentPoint);
            }
            else if (isDrawing)
            {
                UpdateDrawingShape(currentPoint);
            }
        }

        private void ResizeSelectedShape(Point currentPoint)
        {
            if (selectedShape == null || activeResizeMarker == null) return;

            double deltaX = currentPoint.X - resizeStartPoint.X;
            double deltaY = currentPoint.Y - resizeStartPoint.Y;

            // Используем единый подход для всех фигур - изменение ограничивающего прямоугольника
            if (selectedShape is Line line)
            {
                ResizeLineLikeRectangle(line, deltaX, deltaY);
            }
            else if (selectedShape is Polygon polygon)
            {
                ResizePolygonLikeRectangle(polygon, deltaX, deltaY);
            }
            else if (selectedShape is Rectangle rect)
            {
                ResizeRectangle(rect, deltaX, deltaY);
            }
            else if (selectedShape is Ellipse ellipse)
            {
                ResizeEllipse(ellipse, deltaX, deltaY);
            }

            UpdateResizeMarkers();
            resizeStartPoint = currentPoint;
        }

        private void ResizeLineLikeRectangle(Line line, double deltaX, double deltaY)
        {
            // Получаем текущие координаты линии
            double x1 = line.X1;
            double y1 = line.Y1;
            double x2 = line.X2;
            double y2 = line.Y2;

            // Находим границы линии
            double minX = Math.Min(x1, x2);
            double maxX = Math.Max(x1, x2);
            double minY = Math.Min(y1, y2);
            double maxY = Math.Max(y1, y2);
            double width = maxX - minX;
            double height = maxY - minY;

            // Минимальный размер
            double minSize = 5;

            // Обновляем границы в зависимости от активного маркера
            switch (activeResizeMarker.Type)
            {
                case ResizeMarkerType.TopLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    if (height - deltaY > minSize) height -= deltaY;
                    minX += deltaX;
                    minY += deltaY;
                    break;
                case ResizeMarkerType.TopMiddle:
                    if (height - deltaY > minSize) height -= deltaY;
                    minY += deltaY;
                    break;
                case ResizeMarkerType.TopRight:
                    if (width + deltaX > minSize) width += deltaX;
                    if (height - deltaY > minSize) height -= deltaY;
                    minY += deltaY;
                    break;
                case ResizeMarkerType.MiddleLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    minX += deltaX;
                    break;
                case ResizeMarkerType.MiddleRight:
                    if (width + deltaX > minSize) width += deltaX;
                    break;
                case ResizeMarkerType.BottomLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    if (height + deltaY > minSize) height += deltaY;
                    minX += deltaX;
                    break;
                case ResizeMarkerType.BottomMiddle:
                    if (height + deltaY > minSize) height += deltaY;
                    break;
                case ResizeMarkerType.BottomRight:
                    if (width + deltaX > minSize) width += deltaX;
                    if (height + deltaY > minSize) height += deltaY;
                    break;
            }

            // Обновляем новые границы
            maxX = minX + width;
            maxY = minY + height;

            // Определяем, какая точка линии была левой/правой
            bool x1WasLeft = x1 <= x2;
            bool y1WasTop = y1 <= y2;

            // Обновляем координаты линии в соответствии с новыми границами
            if (x1WasLeft)
            {
                line.X1 = minX;
                line.X2 = maxX;
            }
            else
            {
                line.X1 = maxX;
                line.X2 = minX;
            }

            if (y1WasTop)
            {
                line.Y1 = minY;
                line.Y2 = maxY;
            }
            else
            {
                line.Y1 = maxY;
                line.Y2 = minY;
            }
        }

        private void ResizeRectangle(Rectangle rect, double deltaX, double deltaY)
        {
            double left = Canvas.GetLeft(rect);
            double top = Canvas.GetTop(rect);
            double width = rect.Width;
            double height = rect.Height;

            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            // Минимальный размер
            double minSize = 5;

            switch (activeResizeMarker.Type)
            {
                case ResizeMarkerType.TopLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    if (height - deltaY > minSize) height -= deltaY;
                    left += deltaX;
                    top += deltaY;
                    break;
                case ResizeMarkerType.TopMiddle:
                    if (height - deltaY > minSize) height -= deltaY;
                    top += deltaY;
                    break;
                case ResizeMarkerType.TopRight:
                    if (width + deltaX > minSize) width += deltaX;
                    if (height - deltaY > minSize) height -= deltaY;
                    top += deltaY;
                    break;
                case ResizeMarkerType.MiddleLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    left += deltaX;
                    break;
                case ResizeMarkerType.MiddleRight:
                    if (width + deltaX > minSize) width += deltaX;
                    break;
                case ResizeMarkerType.BottomLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    if (height + deltaY > minSize) height += deltaY;
                    left += deltaX;
                    break;
                case ResizeMarkerType.BottomMiddle:
                    if (height + deltaY > minSize) height += deltaY;
                    break;
                case ResizeMarkerType.BottomRight:
                    if (width + deltaX > minSize) width += deltaX;
                    if (height + deltaY > minSize) height += deltaY;
                    break;
            }

            rect.Width = Math.Max(minSize, width);
            rect.Height = Math.Max(minSize, height);
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
        }

        private void ResizeEllipse(Ellipse ellipse, double deltaX, double deltaY)
        {
            double left = Canvas.GetLeft(ellipse);
            double top = Canvas.GetTop(ellipse);
            double width = ellipse.Width;
            double height = ellipse.Height;

            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            // Минимальный размер
            double minSize = 5;

            switch (activeResizeMarker.Type)
            {
                case ResizeMarkerType.TopLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    if (height - deltaY > minSize) height -= deltaY;
                    left += deltaX;
                    top += deltaY;
                    break;
                case ResizeMarkerType.TopMiddle:
                    if (height - deltaY > minSize) height -= deltaY;
                    top += deltaY;
                    break;
                case ResizeMarkerType.TopRight:
                    if (width + deltaX > minSize) width += deltaX;
                    if (height - deltaY > minSize) height -= deltaY;
                    top += deltaY;
                    break;
                case ResizeMarkerType.MiddleLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    left += deltaX;
                    break;
                case ResizeMarkerType.MiddleRight:
                    if (width + deltaX > minSize) width += deltaX;
                    break;
                case ResizeMarkerType.BottomLeft:
                    if (width - deltaX > minSize) width -= deltaX;
                    if (height + deltaY > minSize) height += deltaY;
                    left += deltaX;
                    break;
                case ResizeMarkerType.BottomMiddle:
                    if (height + deltaY > minSize) height += deltaY;
                    break;
                case ResizeMarkerType.BottomRight:
                    if (width + deltaX > minSize) width += deltaX;
                    if (height + deltaY > minSize) height += deltaY;
                    break;
            }

            ellipse.Width = Math.Max(minSize, width);
            ellipse.Height = Math.Max(minSize, height);
            Canvas.SetLeft(ellipse, left);
            Canvas.SetTop(ellipse, top);
        }

        private void ResizePolygonLikeRectangle(Polygon polygon, double deltaX, double deltaY)
        {
            if (polygon.Points.Count == 0) return;

            // Получаем точки полигона
            List<Point> points = polygon.Points.ToList();

            // Находим границы
            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);

            // Старые размеры
            double oldWidth = maxX - minX;
            double oldHeight = maxY - minY;

            // Новые границы (пока такие же как старые)
            double newMinX = minX;
            double newMinY = minY;
            double newMaxX = maxX;
            double newMaxY = maxY;

            // Минимальный размер
            double minSize = 5;

            // Изменяем только одну сторону за раз, в зависимости от маркера
            switch (activeResizeMarker.Type)
            {
                case ResizeMarkerType.TopLeft:
                    if (oldWidth - deltaX > minSize) newMinX += deltaX;
                    if (oldHeight - deltaY > minSize) newMinY += deltaY;
                    break;

                case ResizeMarkerType.TopMiddle:
                    if (oldHeight - deltaY > minSize) newMinY += deltaY;
                    break;

                case ResizeMarkerType.TopRight:
                    if (oldWidth + deltaX > minSize) newMaxX += deltaX;
                    if (oldHeight - deltaY > minSize) newMinY += deltaY;
                    break;

                case ResizeMarkerType.MiddleLeft:
                    if (oldWidth - deltaX > minSize) newMinX += deltaX;
                    break;

                case ResizeMarkerType.MiddleRight:
                    if (oldWidth + deltaX > minSize) newMaxX += deltaX;
                    break;

                case ResizeMarkerType.BottomLeft:
                    if (oldWidth - deltaX > minSize) newMinX += deltaX;
                    if (oldHeight + deltaY > minSize) newMaxY += deltaY;
                    break;

                case ResizeMarkerType.BottomMiddle:
                    if (oldHeight + deltaY > minSize) newMaxY += deltaY;
                    break;

                case ResizeMarkerType.BottomRight:
                    if (oldWidth + deltaX > minSize) newMaxX += deltaX;
                    if (oldHeight + deltaY > minSize) newMaxY += deltaY;
                    break;
            }

            // Новые размеры
            double newWidth = newMaxX - newMinX;
            double newHeight = newMaxY - newMinY;

            // Проверяем минимальные размеры
            if (newWidth < minSize || newHeight < minSize) return;

            // Масштабируем точки
            PointCollection newPoints = new PointCollection();

            // Простой линейный масштаб
            double scaleX = oldWidth > 0 ? newWidth / oldWidth : 1.0;
            double scaleY = oldHeight > 0 ? newHeight / oldHeight : 1.0;

            // Центр старых границ
            double oldCenterX = (minX + maxX) / 2;
            double oldCenterY = (minY + maxY) / 2;

            // Центр новых границ
            double newCenterX = (newMinX + newMaxX) / 2;
            double newCenterY = (newMinY + newMaxY) / 2;

            foreach (Point point in points)
            {
                // Относительные координаты от старого центра
                double relX = point.X - oldCenterX;
                double relY = point.Y - oldCenterY;

                // Масштабируем
                double scaledX = relX * scaleX;
                double scaledY = relY * scaleY;

                // Новые координаты относительно нового центра
                double newX = newCenterX + scaledX;
                double newY = newCenterY + scaledY;

                newPoints.Add(new Point(newX, newY));
            }

            polygon.Points = newPoints;
        }

        private void MoveSelectedShape(Point currentPoint)
        {
            Vector delta = currentPoint - lastMousePosition;

            if (selectedShape is Line line)
            {
                line.X1 += delta.X;
                line.Y1 += delta.Y;
                line.X2 += delta.X;
                line.Y2 += delta.Y;
            }
            else if (selectedShape is Polygon poly && poly.Points.Count > 0)
            {
                PointCollection newPoints = new PointCollection();
                foreach (Point p in poly.Points)
                {
                    newPoints.Add(new Point(p.X + delta.X, p.Y + delta.Y));
                }
                poly.Points = newPoints;
            }
            else
            {
                double left = Canvas.GetLeft(selectedShape);
                double top = Canvas.GetTop(selectedShape);

                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                Canvas.SetLeft(selectedShape, left + delta.X);
                Canvas.SetTop(selectedShape, top + delta.Y);
            }

            lastMousePosition = currentPoint;
            UpdateResizeMarkers();
        }

        private void UpdateDrawingShape(Point currentPoint)
        {
            if (currentShape is Line line)
            {
                line.X2 = currentPoint.X;
                line.Y2 = currentPoint.Y;
            }
            else if (currentShape is Rectangle rect)
            {
                double width = currentPoint.X - startPoint.X;
                double height = currentPoint.Y - startPoint.Y;

                rect.Width = Math.Abs(width);
                rect.Height = Math.Abs(height);

                double left = width >= 0 ? startPoint.X : startPoint.X + width;
                double top = height >= 0 ? startPoint.Y : startPoint.Y + height;

                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
            }
            else if (currentShape is Ellipse ellipse)
            {
                double width = currentPoint.X - startPoint.X;
                double height = currentPoint.Y - startPoint.Y;

                ellipse.Width = Math.Abs(width);
                ellipse.Height = Math.Abs(height);

                double left = width >= 0 ? startPoint.X : startPoint.X + width;
                double top = height >= 0 ? startPoint.Y : startPoint.Y + height;

                Canvas.SetLeft(ellipse, left);
                Canvas.SetTop(ellipse, top);
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isResizing)
            {
                isResizing = false;
                activeResizeMarker = null;
            }
            else if (isMoving)
            {
                isMoving = false;
            }
            else if (isDrawing)
            {
                isDrawing = false;

                if (currentShape != null)
                {
                    if (undoStack.Count >= 5)
                        undoStack.RemoveAt(0);

                    undoStack.Add(currentShape);
                    SelectShape(currentShape);
                }

                currentShape = null;
            }
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;

                double zoomFactor = e.Delta > 0 ? 1.1 : 0.9;
                double newScale = currentScale * zoomFactor;
                newScale = Math.Max(minScale, Math.Min(maxScale, newScale));

                Point mousePos = e.GetPosition(MainScrollViewer);

                double scaleRatio = newScale / currentScale;

                double oldHorizontal = MainScrollViewer.HorizontalOffset;
                double oldVertical = MainScrollViewer.VerticalOffset;

                double mouseInContentX = oldHorizontal + mousePos.X;
                double mouseInContentY = oldVertical + mousePos.Y;

                double newMouseInContentX = mouseInContentX * scaleRatio;
                double newMouseInContentY = mouseInContentY * scaleRatio;

                double newHorizontal = newMouseInContentX - mousePos.X;
                double newVertical = newMouseInContentY - mousePos.Y;

                double maxHorizontal = Math.Max(0, (Canvas1.Width * newScale) - MainScrollViewer.ViewportWidth);
                double maxVertical = Math.Max(0, (Canvas1.Height * newScale) - MainScrollViewer.ViewportHeight);

                newHorizontal = Math.Max(0, Math.Min(newHorizontal, maxHorizontal));
                newVertical = Math.Max(0, Math.Min(newVertical, maxVertical));

                currentScale = newScale;
                Canvas1.LayoutTransform = new ScaleTransform(currentScale, currentScale);

                MainScrollViewer.ScrollToHorizontalOffset(newHorizontal);
                MainScrollViewer.ScrollToVerticalOffset(newVertical);

                ZoomSlider.Value = currentScale;
                UpdateZoomText();

                // Обновляем размеры маркеров при изменении масштаба
                UpdateResizeMarkersSize();
            }
        }

        private void UpdateResizeMarkersSize()
        {
            foreach (var marker in resizeMarkers)
            {
                marker.UpdateSize(currentScale);
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            double newScale = e.NewValue;

            double centerX = MainScrollViewer.ViewportWidth / 2;
            double centerY = MainScrollViewer.ViewportHeight / 2;

            double scaleRatio = newScale / currentScale;

            double oldHorizontal = MainScrollViewer.HorizontalOffset;
            double oldVertical = MainScrollViewer.VerticalOffset;

            double centerInContentX = oldHorizontal + centerX;
            double centerInContentY = oldVertical + centerY;

            double newCenterInContentX = centerInContentX * scaleRatio;
            double newCenterInContentY = centerInContentY * scaleRatio;

            double newHorizontal = newCenterInContentX - centerX;
            double newVertical = newCenterInContentY - centerY;

            double maxHorizontal = Math.Max(0, (Canvas1.Width * newScale) - MainScrollViewer.ViewportWidth);
            double maxVertical = Math.Max(0, (Canvas1.Height * newScale) - MainScrollViewer.ViewportHeight);

            newHorizontal = Math.Max(0, Math.Min(newHorizontal, maxHorizontal));
            newVertical = Math.Max(0, Math.Min(newVertical, maxVertical));

            currentScale = newScale;
            Canvas1.LayoutTransform = new ScaleTransform(currentScale, currentScale);

            MainScrollViewer.ScrollToHorizontalOffset(newHorizontal);
            MainScrollViewer.ScrollToVerticalOffset(newVertical);

            UpdateZoomText();

            // Обновляем размеры маркеров при изменении масштаба
            UpdateResizeMarkersSize();
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            currentScale = 1.0;
            Canvas1.LayoutTransform = new ScaleTransform(currentScale, currentScale);

            MainScrollViewer.ScrollToHorizontalOffset(0);
            MainScrollViewer.ScrollToVerticalOffset(0);

            ZoomSlider.Value = currentScale;
            UpdateZoomText();

            // Обновляем размеры маркеров при сбросе масштаба
            UpdateResizeMarkersSize();
        }

        private void UpdateZoomText()
        {
            int percentage = (int)(currentScale * 100);
            ZoomTextBlock.Text = $"{percentage}%";
        }

        private void UpdateColorPreviews()
        {
            if (StrokePreview != null)
            {
                StrokePreview.Fill = Brushes.Transparent;
                StrokePreview.Stroke = new SolidColorBrush(currentStrokeColor);
                StrokePreview.StrokeThickness = 2;
            }

            if (FillPreview != null)
            {
                FillPreview.Fill = new SolidColorBrush(currentFillColor);
                FillPreview.Stroke = new SolidColorBrush(Colors.Black);
                FillPreview.StrokeThickness = 1;
            }
        }

        private void StrokePreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isStrokeMode = true;
            UpdateMode();
        }

        private void FillPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isStrokeMode = false;
            UpdateMode();
        }

        private void UpdateMode()
        {
            Color activeColor = isStrokeMode ? currentStrokeColor : currentFillColor;
            double[] hsv = RGBtoHSV(activeColor);

            hue = hsv[0];
            saturation = hsv[1];
            value = hsv[2];

            HueSlider.Value = hue;
            UpdateSVSquare();
            UpdateSVMarkerPosition();
            UpdateColor();
        }

        private Shape GetHitShape(Point point)
        {
            HitTestResult result = VisualTreeHelper.HitTest(Canvas1, point);
            return result?.VisualHit as Shape;
        }

        private void Deselect()
        {
            if (selectedShape != null)
            {
                if (shapeOriginals.TryGetValue(selectedShape, out var orig))
                {
                    selectedShape.Stroke = orig.originalStroke;
                    selectedShape.StrokeThickness = orig.originalStrokeThickness;
                    selectedShape.Fill = orig.originalFill;
                }

                selectedShape.Effect = null;
                shapeOriginals.Remove(selectedShape);
                selectedShape = null;
            }
            HideResizeMarkers();
        }

        private void SelectShape(Shape shape)
        {
            if (shape == null) return;

            Deselect();

            selectedShape = shape;
            shapeOriginals[selectedShape] = (selectedShape.Stroke, selectedShape.StrokeThickness, selectedShape.Fill);

            selectedShape.Effect = new DropShadowEffect
            {
                Color = Colors.LightBlue,
                ShadowDepth = 0,
                BlurRadius = 10,
                Opacity = 0.7
            };

            ShowResizeMarkers();
        }

        private Rect GetShapeBounds(Shape shape)
        {
            if (shape is Line line)
            {
                double minX = Math.Min(line.X1, line.X2);
                double minY = Math.Min(line.Y1, line.Y2);
                double maxX = Math.Max(line.X1, line.X2);
                double maxY = Math.Max(line.Y1, line.Y2);
                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
            else if (shape is Polygon polygon && polygon.Points.Count > 0)
            {
                double minX = polygon.Points.Min(p => p.X);
                double minY = polygon.Points.Min(p => p.Y);
                double maxX = polygon.Points.Max(p => p.X);
                double maxY = polygon.Points.Max(p => p.Y);
                return new Rect(minX, minY, maxX - minX, maxY - minY);
            }
            else
            {
                double left = Canvas.GetLeft(shape);
                double top = Canvas.GetTop(shape);
                double width = shape.Width;
                double height = shape.Height;

                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                if (double.IsNaN(width)) width = 0;
                if (double.IsNaN(height)) height = 0;

                return new Rect(left, top, width, height);
            }
        }

        private void ShowResizeMarkers()
        {
            HideResizeMarkers();

            if (selectedShape == null) return;

            Rect bounds = GetShapeBounds(selectedShape);

            // Добавляем отступ для маркеров (зависит от масштаба)
            double padding = 15 / currentScale;
            bounds.Inflate(padding, padding);

            // Создаем маркеры для всех 8 позиций
            CreateResizeMarker(bounds.Left, bounds.Top, ResizeMarkerType.TopLeft);
            CreateResizeMarker(bounds.Left + bounds.Width / 2, bounds.Top, ResizeMarkerType.TopMiddle);
            CreateResizeMarker(bounds.Right, bounds.Top, ResizeMarkerType.TopRight);
            CreateResizeMarker(bounds.Left, bounds.Top + bounds.Height / 2, ResizeMarkerType.MiddleLeft);
            CreateResizeMarker(bounds.Right, bounds.Top + bounds.Height / 2, ResizeMarkerType.MiddleRight);
            CreateResizeMarker(bounds.Left, bounds.Bottom, ResizeMarkerType.BottomLeft);
            CreateResizeMarker(bounds.Left + bounds.Width / 2, bounds.Bottom, ResizeMarkerType.BottomMiddle);
            CreateResizeMarker(bounds.Right, bounds.Bottom, ResizeMarkerType.BottomRight);
        }

        private void CreateResizeMarker(double x, double y, ResizeMarkerType type)
        {
            var marker = new ResizeMarker(type);

            // Устанавливаем размер маркера в зависимости от текущего масштаба
            marker.UpdateSize(currentScale);

            Canvas.SetLeft(marker.Rectangle, x - marker.Rectangle.Width / 2);
            Canvas.SetTop(marker.Rectangle, y - marker.Rectangle.Height / 2);

            Canvas1.Children.Add(marker.Rectangle);
            resizeMarkers.Add(marker);
        }

        private void HideResizeMarkers()
        {
            foreach (var marker in resizeMarkers)
            {
                Canvas1.Children.Remove(marker.Rectangle);
            }
            resizeMarkers.Clear();
        }

        private void UpdateResizeMarkers()
        {
            if (selectedShape != null)
            {
                ShowResizeMarkers();
            }
        }

        private void UpdatePolygonPreview(Point? tempPoint = null)
        {
            if (currentPolygon != null && polygonPoints.Count > 0)
            {
                PointCollection points = new PointCollection();

                foreach (var p in polygonPoints)
                    points.Add(p);

                if (tempPoint.HasValue)
                    points.Add(tempPoint.Value);

                currentPolygon.Points = points;
            }
        }

        private void UpdateSVSquare()
        {
            int width = 150;
            int height = 150;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double s = (double)x / (width - 1);
                    double v = 1.0 - (double)y / (height - 1);
                    Color c = HSVtoRGB(hue, s, v);

                    int index = (y * width + x) * 4;
                    pixels[index] = c.B;
                    pixels[index + 1] = c.G;
                    pixels[index + 2] = c.R;
                    pixels[index + 3] = 255;
                }
            }

            svBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            SVSquare.Children.Remove(SVMarker);
            SVSquare.Children.Add(SVMarker);
        }

        private void UpdateSVMarkerPosition()
        {
            double x = saturation * 150;
            double y = (1.0 - value) * 150;

            Canvas.SetLeft(SVMarker, Math.Max(0, Math.Min(x - 5, 145)));
            Canvas.SetTop(SVMarker, Math.Max(0, Math.Min(y - 5, 145)));
        }

        private Color HSVtoRGB(double h, double s, double v)
        {
            double hh = h;
            double p, q, t, ff;

            if (hh >= 360.0) hh = 0.0;
            hh /= 60.0;

            int i = (int)hh;
            ff = hh - i;
            p = v * (1.0 - s);
            q = v * (1.0 - (s * ff));
            t = v * (1.0 - (s * (1.0 - ff)));

            double r, g, b;

            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }

            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private double[] RGBtoHSV(Color c)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;

            if (delta != 0)
            {
                if (max == r) h = 60 * (((g - b) / delta) % 6);
                else if (max == g) h = 60 * (((b - r) / delta) + 2);
                else h = 60 * (((r - g) / delta) + 4);
            }

            if (h < 0) h += 360;

            double s = max == 0 ? 0 : delta / max;
            double v = max;

            return new double[] { h, s, v };
        }

        private void UpdateColor()
        {
            Color selectedColor = HSVtoRGB(hue, saturation, value);

            if (isStrokeMode)
                currentStrokeColor = selectedColor;
            else
                currentFillColor = selectedColor;

            UpdateColorPreviews();

            if (selectedShape != null)
            {
                if (isStrokeMode || selectedShape is Line)
                    selectedShape.Stroke = new SolidColorBrush(currentStrokeColor);
                else if (selectedShape.Fill != null)
                    selectedShape.Fill = new SolidColorBrush(currentFillColor);

                shapeOriginals[selectedShape] = (
                    selectedShape.Stroke,
                    selectedShape.StrokeThickness,
                    selectedShape.Fill
                );
            }
        }

        private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            hue = e.NewValue;
            UpdateSVSquare();
            UpdateColor();
        }

        private void SVSquare_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            SVSquare.CaptureMouse();
            isDraggingMarker = true;
            UpdateSaturationValueFromMouse(e);
        }

        private void SVSquare_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDraggingMarker)
                UpdateSaturationValueFromMouse(e);
        }

        private void SVSquare_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDraggingMarker)
            {
                UpdateSaturationValueFromMouse(e);
                isDraggingMarker = false;
                SVSquare.ReleaseMouseCapture();
            }
        }

        private void UpdateSaturationValueFromMouse(MouseEventArgs e)
        {
            Point point = e.GetPosition(SVSquare);

            saturation = Math.Max(0, Math.Min(1, point.X / SVSquare.ActualWidth));
            value = 1.0 - Math.Max(0, Math.Min(1, point.Y / SVSquare.ActualHeight));

            UpdateSVMarkerPosition();
            UpdateColor();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.Z)
            {
                e.Handled = true;

                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    PerformRedo();
                else
                    PerformUndo();
            }
            else if (selectedShape != null && e.Key == Key.Delete)
            {
                e.Handled = true;
                Canvas1.Children.Remove(selectedShape);
                undoStack.Remove(selectedShape);

                // Удаляем также все маркеры изменения размера для этой фигуры
                HideResizeMarkers();

                Deselect();
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.D0)
            {
                e.Handled = true;
                ZoomReset_Click(null, null);
            }
            else if (selectedShape != null && Keyboard.IsKeyDown(Key.LeftCtrl) && e.Key == Key.Add)
            {
                // Увеличить выбранную фигуру на 10%
                ScaleSelectedShape(1.1);
                e.Handled = true;
            }
            else if (selectedShape != null && Keyboard.IsKeyDown(Key.LeftCtrl) && e.Key == Key.Subtract)
            {
                // Уменьшить выбранную фигуру на 10%
                ScaleSelectedShape(0.9);
                e.Handled = true;
            }
            else if (selectedShape != null && e.Key == Key.Escape)
            {
                // Отмена выделения
                Deselect();
                e.Handled = true;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.N)
            {
                if (isProcessingFile) return;
                e.Handled = true;
                NewDocument();
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.O)
            {
                if (isProcessingFile) return;
                e.Handled = true;
                OpenSVG();
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key == Key.S)
            {
                if (isProcessingFile) return;
                e.Handled = true;
                SaveSVG();
            }
        }

        private void ScaleSelectedShape(double scaleFactor)
        {
            if (selectedShape == null) return;

            Rect bounds = GetShapeBounds(selectedShape);

            if (selectedShape is Line line)
            {
                // Для линии масштабируем как прямоугольник
                double centerX = (line.X1 + line.X2) / 2;
                double centerY = (line.Y1 + line.Y2) / 2;

                // Вычисляем новые координаты относительно центра
                double newX1 = centerX + (line.X1 - centerX) * scaleFactor;
                double newY1 = centerY + (line.Y1 - centerY) * scaleFactor;
                double newX2 = centerX + (line.X2 - centerX) * scaleFactor;
                double newY2 = centerY + (line.Y2 - centerY) * scaleFactor;

                line.X1 = newX1;
                line.Y1 = newY1;
                line.X2 = newX2;
                line.Y2 = newY2;
            }
            else if (selectedShape is Polygon polygon)
            {
                // Для полигона масштабируем от центра как у прямоугольника
                Point center = new Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height / 2
                );

                PointCollection newPoints = new PointCollection();
                foreach (Point point in polygon.Points)
                {
                    double newX = center.X + (point.X - center.X) * scaleFactor;
                    double newY = center.Y + (point.Y - center.Y) * scaleFactor;
                    newPoints.Add(new Point(newX, newY));
                }
                polygon.Points = newPoints;
            }
            else if (selectedShape is Rectangle rect)
            {
                double left = Canvas.GetLeft(rect);
                double top = Canvas.GetTop(rect);
                double width = rect.Width;
                double height = rect.Height;

                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                double centerX = left + width / 2;
                double centerY = top + height / 2;

                double newWidth = Math.Max(5, width * scaleFactor);
                double newHeight = Math.Max(5, height * scaleFactor);

                double newLeft = centerX - newWidth / 2;
                double newTop = centerY - newHeight / 2;

                rect.Width = newWidth;
                rect.Height = newHeight;
                Canvas.SetLeft(rect, newLeft);
                Canvas.SetTop(rect, newTop);
            }
            else if (selectedShape is Ellipse ellipse)
            {
                double left = Canvas.GetLeft(ellipse);
                double top = Canvas.GetTop(ellipse);
                double width = ellipse.Width;
                double height = ellipse.Height;

                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;

                double centerX = left + width / 2;
                double centerY = top + height / 2;

                double newWidth = Math.Max(5, width * scaleFactor);
                double newHeight = Math.Max(5, height * scaleFactor);

                double newLeft = centerX - newWidth / 2;
                double newTop = centerY - newHeight / 2;

                ellipse.Width = newWidth;
                ellipse.Height = newHeight;
                Canvas.SetLeft(ellipse, newLeft);
                Canvas.SetTop(ellipse, newTop);
            }

            UpdateResizeMarkers();
        }

        private void PerformUndo()
        {
            if (undoStack.Count > 0)
            {
                Shape lastShape = undoStack[undoStack.Count - 1];
                undoStack.RemoveAt(undoStack.Count - 1);
                Canvas1.Children.Remove(lastShape);

                // Если отменяемая фигура была выделена, снимаем выделение
                if (selectedShape == lastShape)
                {
                    Deselect();
                }

                if (redoStack.Count >= 5)
                    redoStack.RemoveAt(0);

                redoStack.Add(lastShape);
            }
        }

        private void PerformRedo()
        {
            if (redoStack.Count > 0)
            {
                Shape lastUndoneShape = redoStack[redoStack.Count - 1];
                redoStack.RemoveAt(redoStack.Count - 1);
                Canvas1.Children.Add(lastUndoneShape);

                if (undoStack.Count >= 5)
                    undoStack.RemoveAt(0);

                undoStack.Add(lastUndoneShape);

                // Автоматически выделяем восстановленную фигуру
                SelectShape(lastUndoneShape);
            }
        }
    }
}