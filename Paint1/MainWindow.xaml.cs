using System;
using System.Windows.Shapes;
using System.Windows;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;

namespace Paint1
{
    public partial class MainWindow : Window
    {
        private bool isDrawing = false;
        private Point startPoint;
        private Shape currentShape;
        private string selectedTool = "Line";
        private ScaleTransform zoomTransform = new ScaleTransform(1.0, 1.0);
        private double hue = 0;
        private double saturation = 0;
        private double value = 0;
        private Color currentStrokeColor = Colors.Black;
        private Color currentFillColor = Colors.Black;
        private bool isStrokeMode = true;
        private bool isDraggingMarker = false;
        private List<Shape> undoStack = new List<Shape>();
        private List<Shape> redoStack = new List<Shape>();
        private WriteableBitmap svBitmap;
        private Image svImage;
        private Shape selectedShape = null;
        private Dictionary<Shape, (Brush originalStroke, double originalStrokeThickness, Brush originalFill, Effect originalEffect)> shapeOriginals = new Dictionary<Shape, (Brush, double, Brush, Effect)>();
        private bool isMoving = false;
        private Vector moveOffset;
        private Point previousMousePoint;

        public MainWindow()
        {
            InitializeComponent();
            Canvas1.LayoutTransform = zoomTransform;
            Canvas1.MouseDown += Canvas_MouseDown;
            Canvas1.MouseMove += Canvas_MouseMove;
            Canvas1.MouseUp += Canvas_MouseUp;
            LineButton.Click += (s, e) => { selectedTool = "Line"; };
            SquareButton.Click += (s, e) => { selectedTool = "Square"; };
            EllipseButton.Click += (s, e) => { selectedTool = "Ellipse"; };
            if (ZoomSlider != null)
                ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
            UpdateColorPreviews();
            InitializeColorPicker();
            double[] hsv = RGBtoHSV(currentStrokeColor);
            hue = hsv[0];
            saturation = hsv[1];
            value = hsv[2];
            HueSlider.Value = hue;
            UpdateSVSquare();
            UpdateSVMarkerPosition();
            this.KeyDown += MainWindow_KeyDown;
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
                Deselect();
            }
        }

        private void PerformUndo()
        {
            if (undoStack.Count > 0)
            {
                Shape lastShape = undoStack[undoStack.Count - 1];
                undoStack.RemoveAt(undoStack.Count - 1);
                Canvas1.Children.Remove(lastShape);
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
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            zoomTransform.ScaleX = e.NewValue;
            zoomTransform.ScaleY = e.NewValue;
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

        private void InitializeColorPicker()
        {
            svBitmap = new WriteableBitmap(150, 150, 96, 96, PixelFormats.Bgr32, null);
            svImage = new Image { Source = svBitmap, Width = 150, Height = 150 };
            SVSquare.Children.Add(svImage);
            UpdateSVSquare();
            UpdateSVMarkerPosition();
            UpdateColor();
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
                shapeOriginals[selectedShape] = (selectedShape.Stroke, selectedShape.StrokeThickness, selectedShape.Fill, shapeOriginals[selectedShape].originalEffect);
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
            saturation = Math.Min(1.0, Math.Max(0.0, point.X / SVSquare.ActualWidth));
            value = 1.0 - Math.Min(1.0, Math.Max(0.0, point.Y / SVSquare.ActualHeight));
            UpdateSVMarkerPosition();
            UpdateColor();
        }

        private void Deselect()
        {
            if (selectedShape != null)
            {
                var orig = shapeOriginals[selectedShape];
                selectedShape.Stroke = orig.originalStroke;
                selectedShape.StrokeThickness = orig.originalStrokeThickness;
                selectedShape.Fill = orig.originalFill;
                selectedShape.Effect = orig.originalEffect;
                shapeOriginals.Remove(selectedShape);
                selectedShape = null;
            }
        }

        private void SelectShape(Shape shape)
        {
            if (selectedShape != null)
                Deselect();
            selectedShape = shape;
            shapeOriginals[selectedShape] = (selectedShape.Stroke, selectedShape.StrokeThickness, selectedShape.Fill, selectedShape.Effect);
            selectedShape.Effect = new DropShadowEffect
            {
                Color = Colors.Cyan,
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 1.0
            };
        }

        private Shape GetHitShape(Point point)
        {
            HitTestResult result = VisualTreeHelper.HitTest(Canvas1, point);
            if (result != null && result.VisualHit is Shape shape)
                return shape;
            return null;
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point clickPoint = e.GetPosition(Canvas1);
            Shape hitShape = GetHitShape(clickPoint);
            if (hitShape != null && !isDrawing)
            {
                SelectShape(hitShape);
                double left = Canvas.GetLeft(hitShape);
                double top = Canvas.GetTop(hitShape);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top)) top = 0;
                moveOffset = new Vector(clickPoint.X - left, clickPoint.Y - top);
                previousMousePoint = clickPoint;
                isMoving = true;
            }
            else
            {
                Deselect();
                startPoint = clickPoint;
                previousMousePoint = clickPoint;
                isDrawing = true;
                switch (selectedTool)
                {
                    case "Line":
                        currentShape = new Line { X1 = startPoint.X, Y1 = startPoint.Y, Stroke = new SolidColorBrush(currentStrokeColor), StrokeThickness = 2 };
                        break;
                    case "Square":
                        currentShape = new Rectangle { Width = 0, Height = 0, Stroke = new SolidColorBrush(currentStrokeColor), Fill = new SolidColorBrush(currentFillColor), StrokeThickness = 2 };
                        break;
                    case "Ellipse":
                        currentShape = new Ellipse { Width = 0, Height = 0, Stroke = new SolidColorBrush(currentStrokeColor), Fill = new SolidColorBrush(currentFillColor), StrokeThickness = 2 };
                        break;
                }
                Canvas1.Children.Add(currentShape);
                shapeOriginals[currentShape] = (currentShape.Stroke, currentShape.StrokeThickness, currentShape.Fill, currentShape.Effect);
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentPoint = e.GetPosition(Canvas1);
            if (isMoving && selectedShape != null && !isDrawing)
            {
                if (selectedShape is Line line)
                {
                    Vector delta = currentPoint - previousMousePoint;
                    line.X1 += delta.X;
                    line.Y1 += delta.Y;
                    line.X2 += delta.X;
                    line.Y2 += delta.Y;
                    previousMousePoint = currentPoint;
                    Canvas.SetLeft(line, 0);
                    Canvas.SetTop(line, 0);
                }
                else
                {
                    double newLeft = currentPoint.X - moveOffset.X;
                    double newTop = currentPoint.Y - moveOffset.Y;
                    Canvas.SetLeft(selectedShape, newLeft);
                    Canvas.SetTop(selectedShape, newTop);
                }
            }
            else if (isDrawing && currentShape != null)
            {
                Point endPoint = e.GetPosition(Canvas1);
                if (currentShape is Line line)
                {
                    line.X2 = endPoint.X;
                    line.Y2 = endPoint.Y;
                }
                else if (currentShape is Rectangle rect)
                {
                    double minX = Math.Min(startPoint.X, endPoint.X);
                    double minY = Math.Min(startPoint.Y, endPoint.Y);
                    double width = Math.Abs(endPoint.X - startPoint.X);
                    double height = Math.Abs(endPoint.Y - startPoint.Y);
                    rect.Width = width;
                    rect.Height = height;
                    Canvas.SetLeft(rect, minX);
                    Canvas.SetTop(rect, minY);
                }
                else if (currentShape is Ellipse ellipse)
                {
                    double minX = Math.Min(startPoint.X, endPoint.X);
                    double minY = Math.Min(startPoint.Y, endPoint.Y);
                    double width = Math.Abs(endPoint.X - startPoint.X);
                    double height = Math.Abs(endPoint.Y - startPoint.Y);
                    ellipse.Width = width;
                    ellipse.Height = height;
                    Canvas.SetLeft(ellipse, minX);
                    Canvas.SetTop(ellipse, minY);
                }
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDrawing)
            {
                isDrawing = false;
                if (undoStack.Count >= 5)
                    undoStack.RemoveAt(0);
                undoStack.Add(currentShape);
                redoStack.Clear();
            }
            if (isMoving)
            {
                isMoving = false;
            }
        }
    }
}
