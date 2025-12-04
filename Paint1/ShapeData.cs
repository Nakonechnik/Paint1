using System.Windows;
using System.Windows.Media;

namespace VectorEditor
{
    public class ShapeData
    {
        public string Type { get; set; } // "Rectangle", "Ellipse", "Line", "Polygon"
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; } // Для линии
        public double Y2 { get; set; } // Для линии
        public double Width { get; set; } // Для прямоугольника и эллипса
        public double Height { get; set; } // Для прямоугольника и эллипса
        public Color FillColor { get; set; }
        public Color StrokeColor { get; set; }
        public Point[] Points { get; set; } // Для полигона
    }
}
